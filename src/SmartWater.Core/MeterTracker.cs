namespace SmartWater.Core;

/// <summary>معايرة عدّاد واحد (إحداثيات الإطار الكامل بالبكسل).</summary>
/// <param name="Cx">محور دوران العقرب الكبير السفلي</param>
/// <param name="Radius">نصف قطر قرص العدّاد الصغير الذي يدور فيه العقرب</param>
public sealed record MeterConfig(string Name, double Cx, double Cy, double Radius);

public sealed record TrackerParams
{
    public double BandInner { get; init; } = 0.33;
    public double BandOuter { get; init; } = 0.52;
    public int SmoothDeg { get; init; } = 15;
    public double MinContrast { get; init; } = 0.025;
    public double LockContrast { get; init; } = 0.05;
    public double MaxDegPerSec { get; init; } = 900.0;
    public double BaseFwdDeg { get; init; } = 12.0;
    public double AccelFactor { get; init; } = 3.0;
    public double WideAfterSec { get; init; } = 1.0;
    public double BackDeg { get; init; } = 8.0;
    public double DeadbandDeg { get; init; } = 1.5;
    public int ShakeSearch { get; init; } = 6;
    public double RestAccelDegPerSec { get; init; } = 150.0;
    public double BackWideDeg { get; init; } = 20.0;
    public double RelLow { get; init; } = 0.5;
    public double RelHigh { get; init; } = 3.0;
}

public sealed class MeterSample
{
    public double T { get; }
    /// <summary>false: لا قياس موثوق في هذا الإطار (ليس توقفًا!)</summary>
    public bool Valid { get; }
    /// <summary>الحركة المتراكمة المثبّتة بالدرجات (تتزايد فقط)</summary>
    public double AngleDeg { get; }
    /// <summary>زاوية العقرب 0..360 (0=أعلى)</summary>
    public double RawAngleDeg { get; }
    public double Contrast { get; }
    public double ShiftX { get; }
    public double ShiftY { get; }
    /// <summary>قفزة بعد فقدان طويل (بحث واسع) — الدقة أقل</summary>
    public bool UncertainJump { get; }

    public MeterSample(double t, bool valid, double angleDeg, double rawAngleDeg, double contrast,
                       double shiftX, double shiftY, bool uncertainJump)
    {
        T = t; Valid = valid; AngleDeg = angleDeg; RawAngleDeg = rawAngleDeg; Contrast = contrast;
        ShiftX = shiftX; ShiftY = shiftY; UncertainJump = uncertainJump;
    }
}

/// <summary>
/// يتتبّع العقرب الكبير لعدّاد واحد ويحسب الحركة المتراكمة: تعويض الاهتزاز، ملف زاوي، اختيار القاع ضمن نافذة
/// تنبؤ أمامية، فكّ اللف بالفرق الأصغر بالإشارة (إصلاح الدورات الوهمية)، ومنطقة ميتة صغيرة.
/// </summary>
public sealed class MeterTracker
{
    private const int CentroidHalf = 22;

    public MeterConfig Cfg { get; }
    private readonly TrackerParams _p;
    private readonly HandProfile _profile;
    private readonly ShakeCompensator _shake;
    private readonly double[] _prof = new double[HandProfile.N];

    private double _sx, _sy;
    private bool _locked;
    private double _lastMod, _lastValidT, _rawCum, _committed;
    private double _vEst;   // سرعة مقدّرة (°/ث)
    private double _handC;  // تباين العقرب المعتاد

    public MeterTracker(MeterConfig cfg, TrackerParams? p = null, GrayFrame? reference = null)
    {
        Cfg = cfg;
        _p = p ?? new TrackerParams();
        _profile = new HandProfile(_p.SmoothDeg);
        _shake = new ShakeCompensator(search: _p.ShakeSearch);
        if (reference != null) _shake.SetReference(reference, cfg.Cx, cfg.Cy, cfg.Radius);
    }

    public double TotalDegrees => _committed;

    public void Reset()
    {
        _locked = false; _rawCum = 0; _committed = 0; _sx = 0; _sy = 0; _vEst = 0; _handC = 0;
    }

    public MeterSample Update(GrayFrame f, double t)
    {
        if (!_shake.HasRef) _shake.SetReference(f, Cfg.Cx, Cfg.Cy, Cfg.Radius);
        else
        {
            var sh = _shake.Estimate(f, _sx, _sy);
            if (sh != null) { _sx = sh.X; _sy = sh.Y; }
        }

        bool ok = _profile.Compute(f, Cfg.Cx + _sx, Cfg.Cy + _sy, Cfg.Radius, _p.BandInner, _p.BandOuter, _prof);
        if (!ok) return Invalid(t);

        if (!_locked)
        {
            int bi = 0;
            for (int a = 1; a < HandProfile.N; a++) if (_prof[a] < _prof[bi]) bi = a;
            double c0 = -_prof[bi];
            if (c0 < _p.LockContrast) return Invalid(t);
            _locked = true;
            _lastMod = Refine(bi);
            _lastValidT = t;
            _handC = c0;
            return Valid(t, c0, false);
        }

        double lost = t - _lastValidT;
        bool wide = lost > _p.WideAfterSec;
        double fwd = wide
            ? Math.Min(359.0, _p.MaxDegPerSec * lost + 10.0)
            : Math.Min(120.0, _p.BaseFwdDeg + _p.AccelFactor * _vEst * lost + _p.RestAccelDegPerSec * lost);
        int center = K.RoundInt(_lastMod);
        int lo = -K.RoundInt(_p.BackDeg);
        int hi = (int)Math.Ceiling(fwd);
        int best = -1;
        double bv = double.MaxValue;
        if (wide || _handC <= 0.0)
        {
            for (int d = lo; d <= hi; d++)
            {
                int a = K.Mod(center + d, 360);
                if (_prof[a] < bv) { bv = _prof[a]; best = a; }
            }
        }
        else
        {
            // أثناء التتبّع: أعمق قاع محلي *يشبه العقرب* (تباين بين RelLow و RelHigh من المعتاد)
            double lowC = Math.Max(_p.MinContrast, _p.RelLow * _handC);
            double highC = _p.RelHigh * _handC;
            for (int d = lo; d <= hi; d++)
            {
                int a = K.Mod(center + d, 360);
                double c = -_prof[a];
                if (c < lowC || c > highC || !IsLocalMin(a)) continue;
                if (_prof[a] < bv) { bv = _prof[a]; best = a; }
            }
            if (best < 0) return Invalid(t);
        }
        double contrast = -bv;
        if (contrast < (wide ? _p.LockContrast : _p.MinContrast)) return Invalid(t);

        double ang = Refine(best);
        double diff = ang - _lastMod;
        if (wide)
            diff = (((diff + _p.BackWideDeg) % 360.0) + 360.0) % 360.0 - _p.BackWideDeg;
        else
            diff = (((diff + 180.0) % 360.0) + 360.0) % 360.0 - 180.0;

        if (!wide && _handC > 0.0)
        {
            double gate = Math.Max(30.0, 2.0 * _vEst * lost + 25.0);
            if (diff > gate && contrast > 2.0 * _handC) return Invalid(t);
        }
        if (!wide && lost > 1e-3)
        {
            double step = Math.Max(0.0, diff) / lost;
            _vEst = Math.Max(0.75 * _vEst + 0.25 * step, step);
        }
        else if (wide)
        {
            _vEst = Math.Min(_p.MaxDegPerSec, Math.Max(0.0, diff) / lost);
        }
        _rawCum += diff;
        _lastMod = ((ang % 360.0) + 360.0) % 360.0;
        _lastValidT = t;
        _handC = wide ? contrast : 0.8 * _handC + 0.2 * contrast;
        if (_rawCum - _committed > _p.DeadbandDeg) _committed = _rawCum;
        return Valid(t, contrast, wide);
    }

    /// <summary>مركز ثقل القاع (فوق نصف العمق) حول الفهرس.</summary>
    private double Refine(int i)
    {
        int n = HandProfile.N;
        double c = i;
        for (int rep = 0; rep < 2; rep++)
        {
            int ci = (int)Math.Floor(c + 0.5);
            double depth = -_prof[K.Mod(ci, n)];
            double thr = 0.5 * depth;
            double sw = 0, sd = 0;
            for (int d = -CentroidHalf; d <= CentroidHalf; d++)
            {
                double w = -_prof[K.Mod(ci + d, n)] - thr;
                if (w > 0) { sw += w; sd += w * d; }
            }
            if (sw > 0) c = ci + sd / sw;
        }
        return c;
    }

    /// <summary>قاع محلي حقيقي ضمن ±8°.</summary>
    private bool IsLocalMin(int a)
    {
        double v = _prof[a];
        for (int k = 1; k <= 8; k++)
        {
            if (_prof[K.Mod(a - k, 360)] < v || _prof[K.Mod(a + k, 360)] < v) return false;
        }
        return true;
    }

    private MeterSample Valid(double t, double c, bool wide) =>
        new(t, true, _committed, _lastMod, c, _sx, _sy, wide);

    private MeterSample Invalid(double t) =>
        new(t, false, _committed, _lastMod, 0.0, _sx, _sy, false);
}
