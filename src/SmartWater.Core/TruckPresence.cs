namespace SmartWater.Core;

public sealed record PresenceParams
{
    public float PixelDiff { get; init; } = 28f;      // فرق السطوع (0..255) ليُعدّ الموضع متغيّرًا عن الخلفية
    public double EnterFrac { get; init; } = 0.10;    // نسبة المواضع المتغيّرة لاعتبار وايت موجودًا
    public double LeaveFrac { get; init; } = 0.05;    // أقل من هذا = المنطقة فارغة
    public int EnterSamples { get; init; } = 3;       // لقطات متتالية للتأكد من الدخول
    public int LeaveSamples { get; init; } = 4;       // لقطات متتالية للتأكد من الخروج
    public float BgRate { get; init; } = 0.05f;       // سرعة تحديث الخلفية (والمنطقة فارغة فقط)
    public double ReplaceFrac { get; init; } = 0.5;   // تغيّر ≥ هذه النسبة من مواضع الوايت المستقر السابق = وايت آخر
    public double StableFrac { get; init; } = 0.03;   // تغيّر بين لقطتين متتاليتين أقل من هذا = الوايت ساكن
    /// <summary>منطقة التعبئة كنِسَب من الصورة: x, y, w, h (null = كل الصورة).</summary>
    public double[]? Zone { get; init; }
}

public abstract record PresenceEvent(long TMs)
{
    /// <summary>دخل وايت. TMs = وقت أول لقطة متغيّرة في سلسلة التأكيد.</summary>
    public sealed record Entered(long TMs) : PresenceEvent(TMs);
    /// <summary>غادر الوايت. TMs = وقت أول لقطة فارغة في سلسلة التأكيد.</summary>
    public sealed record Left(long TMs) : PresenceEvent(TMs);
    /// <summary>استُبدل الوايت بآخر دون أن تفرغ المنطقة لفترة تُرصد.</summary>
    public sealed record Replaced(long TMs) : PresenceEvent(TMs);
}

/// <summary>
/// كشف وجود وايت في منطقة التعبئة من لقطات القناة 7 (بلا قراءة لوحات): مقارنة شبكة رمادية 64×48 بصورة الخلفية
/// الفارغة. الخلفية تُحدَّث ببطء ما دامت المنطقة فارغة. الأوقات المُمرَّرة هي أوقات الجهاز (ms).
/// </summary>
public sealed class TruckPresenceDetector
{
    public const int W = 64, H = 48;
    private readonly PresenceParams _p;
    private readonly int[] _cells;

    private float[]? _bg, _prev, _stableRef;
    private int[] _stableMask = Array.Empty<int>();
    private int _stableRun; private long _stableStart;

    // مرجع الوايت أثناء تدفق الماء (ساكن تحت القصبة): يُقاس به أي تغيّر لاحق في المنطقة
    private float[]? _ref;
    private int[] _refMask = Array.Empty<int>();
    private int _enterRun; private long _enterStart;
    private int _leaveRun; private long _leaveStart;

    /// <summary>نسبة مواضع الوايت المرجعي التي تغيّرت الآن (0 إن لا مرجع). ≥ ~0.35 = وايت آخر أو غادر.</summary>
    public double RefChange { get; private set; }
    public bool Present { get; private set; }
    /// <summary>آخر نسبة تغيّر محسوبة (للعرض والتشخيص).</summary>
    public double LastFraction { get; private set; }

    public TruckPresenceDetector(PresenceParams? p = null)
    {
        _p = p ?? new PresenceParams();
        var z = _p.Zone;
        int x0 = Math.Clamp((int)((z?[0] ?? 0.0) * W), 0, W - 1);
        int y0 = Math.Clamp((int)((z?[1] ?? 0.0) * H), 0, H - 1);
        int x1 = Math.Clamp(x0 + (int)((z?[2] ?? 1.0) * W), x0 + 1, W);
        int y1 = Math.Clamp(y0 + (int)((z?[3] ?? 1.0) * H), y0 + 1, H);
        var l = new List<int>();
        for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) l.Add(y * W + x);
        _cells = l.ToArray();
    }

    /// <summary>تصغير بمتوسط المساحة إلى شبكة 64×48.</summary>
    public static float[] Grid(byte[] gray, int w, int h)
    {
        var o = new float[W * H];
        for (int dy = 0; dy < H; dy++)
        {
            int sy0 = dy * h / H, sy1 = Math.Min(Math.Max(sy0 + 1, (dy + 1) * h / H), h);
            for (int dx = 0; dx < W; dx++)
            {
                int sx0 = dx * w / W, sx1 = Math.Min(Math.Max(sx0 + 1, (dx + 1) * w / W), w);
                long s = 0; int n = 0;
                for (int y = sy0; y < sy1; y++) for (int x = sx0; x < sx1; x++) { s += gray[y * w + x]; n++; }
                o[dy * W + dx] = (float)s / n;
            }
        }
        return o;
    }

    /// <summary>يعتمد هذه الصورة خلفيةً فارغة (يطلبها المستخدم عند خلو المنطقة).</summary>
    public void SetBackground(byte[] gray, int w, int h)
    {
        _bg = Grid(gray, w, h); Present = false; _enterRun = 0; _leaveRun = 0; ResetStable();
    }

    /// <summary>يعتمد آخر لقطة خلفيةً (إعادة ضبط تلقائية إن بقيت حالة «موجود» عالقة بلا تدفق مدة طويلة).</summary>
    public void RebaseToLast()
    {
        if (_prev == null) return;
        _bg = (float[])_prev.Clone(); Present = false; _enterRun = 0; _leaveRun = 0; ResetStable();
    }

    /// <summary>يثبّت آخر لقطة مرجعًا للوايت الحالي (استدعِها أثناء تدفق الماء).</summary>
    public void SetReference()
    {
        if (_prev == null || _bg == null) return;
        var m = MaskOf(_prev, _bg);
        if (m.Length < 12) return;                   // وايت صغير جدًا في المنطقة: لا نثق بالمقارنة
        _ref = (float[])_prev.Clone(); _refMask = m;
    }

    public void ClearReference() { _ref = null; _refMask = Array.Empty<int>(); RefChange = 0.0; }

    private void ResetStable() { _stableRef = null; _stableMask = Array.Empty<int>(); _stableRun = 0; }

    public bool HasBackground => _bg != null;

    public PresenceEvent? Update(long tMs, byte[] gray, int w, int h)
    {
        var g = Grid(gray, w, h);
        var b = _bg;
        var pg = _prev;
        _prev = g;
        if (b == null) { _bg = (float[])g.Clone(); return null; }   // أول لقطة تُفترض فارغة إلى أن يعيّن المستخدم خلفية
        var rf = _ref;
        RefChange = rf != null && _refMask.Length > 0
            ? (double)_refMask.Count(i => Math.Abs(g[i] - rf[i]) > _p.PixelDiff) / _refMask.Length : 0.0;
        double f = Changed(g, b);
        LastFraction = f;
        if (!Present)
        {
            if (f >= _p.EnterFrac)
            {
                if (_enterRun == 0) _enterStart = tMs;
                _enterRun++;
                if (_enterRun >= _p.EnterSamples)
                {
                    Present = true; _enterRun = 0; _leaveRun = 0; ResetStable();
                    return new PresenceEvent.Entered(_enterStart);
                }
            }
            else
            {
                _enterRun = 0;
                for (int i = 0; i < b.Length; i++) b[i] += _p.BgRate * (g[i] - b[i]);   // تتبّع تغيّر الإضاءة البطيء
            }
            return null;
        }
        // الوايت موجود
        if (f < _p.LeaveFrac)
        {
            if (_leaveRun == 0) _leaveStart = tMs;
            _leaveRun++;
            _stableRun = 0;
            if (_leaveRun >= _p.LeaveSamples) { Present = false; _leaveRun = 0; ResetStable(); return new PresenceEvent.Left(_leaveStart); }
            return null;
        }
        _leaveRun = 0;
        // الاستبدال لا يُقاس أثناء حركة الوايت، بل بين وضعين *مستقرين*: لقطات متتالية شبه متطابقة
        if (pg != null && Changed(g, pg) < _p.StableFrac)
        {
            if (_stableRun == 0) _stableStart = tMs;
            _stableRun++;
        }
        else _stableRun = 0;
        if (_stableRun == _p.EnterSamples)
        {
            var stRef = _stableRef;
            bool replaced = stRef != null && DiffersFromRef(g, stRef) >= _p.ReplaceFrac;
            _stableRef = g; _stableMask = MaskOf(g, b);
            if (replaced) return new PresenceEvent.Replaced(_stableStart);
        }
        return null;
    }

    private int[] MaskOf(float[] g, float[] b) => _cells.Where(i => Math.Abs(g[i] - b[i]) > _p.PixelDiff).ToArray();

    private double DiffersFromRef(float[] g, float[] e)
    {
        if (_stableMask.Length == 0) return 0.0;
        int n = 0;
        foreach (var i in _stableMask) if (Math.Abs(g[i] - e[i]) > _p.PixelDiff) n++;
        return (double)n / _stableMask.Length;
    }

    private double Changed(float[] a, float[] b)
    {
        int n = 0;
        foreach (var i in _cells) if (Math.Abs(a[i] - b[i]) > _p.PixelDiff) n++;
        return (double)n / _cells.Length;
    }
}

/// <summary>
/// كاشف «وايت جديد»: بعد توقف تدفق الماء، إن تغيّرت منطقة الوايت عن مرجعه في Need لقطات متتالية فالجلسة انتهت.
/// لا يعتمد على خروج المنطقة فارغة.
/// </summary>
public sealed class SwapGuard
{
    private readonly double _frac; private readonly int _need; private int _run;
    public SwapGuard(double frac = 0.35, int need = 2) { _frac = frac; _need = need; }
    public void Reset() => _run = 0;
    /// <summary>true = تغيّر الوايت: أغلق الجلسة الحالية.</summary>
    public bool Update(double refChange) { _run = refChange >= _frac ? _run + 1 : 0; return _run >= _need; }
}
