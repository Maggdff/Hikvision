namespace SmartWater.Core;

public enum SwingType { Meter, Full }

public static class Barrel
{
    /// <summary>كل دورة كاملة للعقرب (360°) = نصف برميل، أي 720° = برميل واحد.</summary>
    public const double DegPerBarrel = 720.0;
}

public sealed class MeterPart
{
    public int Meter { get; }
    public double Degrees { get; }
    public double StartT { get; }
    public double EndT { get; }
    public double DegPerBarrel { get; }
    public double Barrels => Degrees / DegPerBarrel;

    public MeterPart(int meter, double degrees, double startT, double endT, double degPerBarrel = Barrel.DegPerBarrel)
    {
        Meter = meter; Degrees = degrees; StartT = startT; EndT = endT; DegPerBarrel = degPerBarrel;
    }
}

public sealed class SwingResult
{
    public SwingType Type { get; }
    public IReadOnlyList<MeterPart> Parts { get; }
    public string Notes { get; }
    /// <summary>إن وُجدت فهي الإجمالي المعتمد بدل مجموع العدّادين (عدّادان على الماء نفسه).</summary>
    public double? TotalOverride { get; }
    public double StartT => Parts.Min(p => p.StartT);
    public double EndT => Parts.Max(p => p.EndT);
    public double TotalBarrels => TotalOverride ?? Parts.Sum(p => p.Barrels);

    public SwingResult(SwingType type, IReadOnlyList<MeterPart> parts, string notes, double? totalOverride = null)
    {
        Type = type; Parts = parts; Notes = notes; TotalOverride = totalOverride;
    }
}

public sealed record SwingParams
{
    public double StartDeg { get; init; } = 6.0;
    public double StartWindowSec { get; init; } = 2.0;
    public double StopDeg { get; init; } = 3.0;
    public double StopWindowSec { get; init; } = 3.0;
    public double MinSwingDeg { get; init; } = 20.0;
    public double EndToleranceSec { get; init; } = 3.0;
    public double MinOverlapSec { get; init; } = 1.0;
    public double GapSec { get; init; } = 1.5;
    /// <summary>مهلة التوقف قبل اعتماد نهاية الزبون (0 = تُسجَّل الزفة فور سكون العدّادين).</summary>
    public double ResolveGraceSec { get; init; } = 0.0;
    public double DegPerBarrel { get; init; } = Barrel.DegPerBarrel;
}

/// <summary>
/// آلة حالات الزفات (نقل حرفي لمنطق SwingEngine.kt): حلقة = زبون واحد؛ زفة عداد أو زفة كاملة؛
/// انقطاع البث/فقدان التتبع لا يُحسب توقفًا.
/// </summary>
public sealed class SwingEngine
{
    private sealed class MS
    {
        public bool Active;
        public double Sa = double.NaN, St;
        public double Rise = double.NaN;
        public double StartA, StartT;
        public double Xa, Xt;
        public double LastA = double.NaN, LastT;
        public bool PendingGap;
        public bool Uncertain;
    }

    private sealed record Seg(int Meter, double StartT, double EndT, double Deg, bool Uncertain, bool Truncated);

    private const double RiseDeg = 0.5;
    private readonly int _meters;
    private readonly SwingParams _p;
    private readonly Action<SwingResult> _onResult;
    private readonly MS[] _ms;
    private readonly List<Seg> _finished = new();
    private bool _episodeOpen, _quiet;
    private double _graceElapsed;
    private double _prevT = double.NaN;

    public int IgnoredBlips { get; private set; }
    public Action<double>? OnEpisodeStart { get; set; }
    public Action? OnEpisodeCancel { get; set; }

    public SwingEngine(int meters, SwingParams p, Action<SwingResult> onResult)
    {
        _meters = meters; _p = p; _onResult = onResult;
        _ms = Enumerable.Range(0, meters).Select(_ => new MS()).ToArray();
    }

    public double? GraceRemaining() =>
        _p.ResolveGraceSec > 0.0 && _quiet && _finished.Count > 0
            ? Math.Max(0.0, _p.ResolveGraceSec - _graceElapsed) : null;

    public double? LastMotionT() => _finished.Count == 0 ? null : _finished.Max(s => s.EndT);
    public bool IsEpisodeOpen() => _episodeOpen;

    public double? FirstMotionT()
    {
        double? r = null;
        foreach (var s in _finished) r = r == null ? s.StartT : Math.Min(r.Value, s.StartT);
        foreach (var m in _ms) if (m.Active) r = r == null ? m.StartT : Math.Min(r.Value, m.StartT);
        return r;
    }

    public bool IsActive(int i) => _ms[i].Active;
    public double CurrentDegrees(int i) => _ms[i].Active ? _ms[i].LastA - _ms[i].StartA : 0.0;
    public double EpisodeDegrees(int i) => _finished.Where(s => s.Meter == i).Sum(s => s.Deg) + CurrentDegrees(i);

    /// <summary>تُستدعى لكل إطار؛ العنصر null أو غير صالح = لا قياس (لا يُعدّ توقفًا).</summary>
    public void Update(double t, IReadOnlyList<MeterSample?> samples)
    {
        double dt = double.IsNaN(_prevT) ? 0.0 : t - _prevT;
        _prevT = t;
        for (int i = 0; i < _meters; i++) Step(i, t, i < samples.Count ? samples[i] : null);
        bool anyActive = _ms.Any(m => m.Active);
        if (!_episodeOpen && anyActive) { _episodeOpen = true; OnEpisodeStart?.Invoke(t); }
        if (anyActive) { _quiet = false; _graceElapsed = 0.0; return; }
        if (_p.ResolveGraceSec <= 0.0) { EndEpisode(); return; }
        if (_finished.Count == 0)
        {
            _quiet = false;
            if (_episodeOpen) { _episodeOpen = false; OnEpisodeCancel?.Invoke(); }
            return;
        }
        if (!_quiet)
        {
            _quiet = true;
            _graceElapsed = Math.Max(0.0, t - _finished.Max(s => s.EndT));
        }
        else if (dt > 0.0 && samples.Any(s => s?.Valid == true))
        {
            _graceElapsed += Math.Min(dt, _p.GapSec);
        }
        if (_graceElapsed >= _p.ResolveGraceSec) Resolve();
    }

    private void EndEpisode()
    {
        if (_finished.Count > 0) Resolve();
        else if (_episodeOpen) { _episodeOpen = false; OnEpisodeCancel?.Invoke(); }
    }

    /// <summary>إشعار صريح بانقطاع البث.</summary>
    public void MarkGap() { foreach (var m in _ms) m.PendingGap = true; }

    /// <summary>نهاية الجلسة: تُغلق الحركات الجارية وتُسجَّل مع ملاحظة.</summary>
    public void Flush()
    {
        for (int i = 0; i < _meters; i++)
        {
            var m = _ms[i];
            if (m.Active) Close(i, m, m.LastA, Math.Max(m.Xt, m.LastT), "انتهى البث أثناء الحركة");
        }
        EndEpisode();
    }

    private void Step(int i, double t, MeterSample? s)
    {
        var m = _ms[i];
        if (s == null || !s.Valid) { m.PendingGap = true; return; }
        double a = s.AngleDeg;
        if (double.IsNaN(m.LastA))
        {
            m.Sa = a; m.St = t; m.Rise = double.NaN; m.LastA = a; m.LastT = t; return;
        }
        bool gap = m.PendingGap || (t - m.LastT) > _p.GapSec;
        if (s.UncertainJump) m.Uncertain = true;
        m.PendingGap = false;

        if (!m.Active)
        {
            if (double.IsNaN(m.Rise) && a - m.Sa > RiseDeg) m.Rise = t;
            if (a - m.Sa >= _p.StartDeg)
            {
                m.Active = true;
                m.StartA = m.Sa; m.StartT = double.IsNaN(m.Rise) ? m.St : m.Rise;
                m.Xa = a; m.Xt = t;
                m.Uncertain = s.UncertainJump || gap;
            }
            else if (t - m.St >= _p.StartWindowSec || gap)
            {
                m.Sa = a; m.St = t; m.Rise = double.NaN;
            }
        }
        else
        {
            if (a - m.Xa >= _p.StopDeg) { m.Xa = a; m.Xt = t; }
            else if (gap) { m.Xa = a; m.Xt = t; }
            else if (t - m.Xt >= _p.StopWindowSec) Close(i, m, a, m.Xt, "");
        }
        m.LastA = a; m.LastT = t;
    }

    private void Close(int i, MS m, double endA, double endT, string note)
    {
        double deg = endA - m.StartA;
        if (deg < _p.MinSwingDeg) IgnoredBlips++;
        else _finished.Add(new Seg(i, m.StartT, endT, deg, m.Uncertain, note.Length > 0));
        m.Active = false;
        m.Sa = m.LastA; m.St = m.LastT; m.Rise = double.NaN;
        m.Uncertain = false;
    }

    private void Resolve()
    {
        var segs = _finished.OrderBy(s => s.StartT).ToList();
        _finished.Clear();
        _episodeOpen = false; _quiet = false; _graceElapsed = 0.0;
        var parts = segs.GroupBy(s => s.Meter).OrderBy(g => g.Key)
            .Select(g => new MeterPart(g.Key, g.Sum(x => x.Deg), g.Min(x => x.StartT), g.Max(x => x.EndT), _p.DegPerBarrel))
            .ToList();
        var type = parts.Count >= 2 ? SwingType.Full : SwingType.Meter;
        _onResult(new SwingResult(type, parts, NoteOf(segs)));
    }

    private static string NoteOf(List<Seg> l)
    {
        var n = new List<string>();
        if (l.Any(s => s.Truncated)) n.Add("انتهى البث أثناء الحركة (زفة غير مكتملة)");
        if (l.Any(s => s.Uncertain)) n.Add("تحتوي فجوة/فقدان تتبع — الكمية تقريبية");
        return string.Join("؛ ", n);
    }
}
