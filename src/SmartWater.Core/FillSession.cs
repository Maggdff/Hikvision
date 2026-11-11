namespace SmartWater.Core;

/// <summary>تفرّع = كل عدّاد يقيس ماءً مختلفًا فيُجمعان؛ تسلسل = الماء نفسه فيُؤخذ الأكبر.</summary>
public enum Wiring { Parallel, Series }

public static class WiringExt
{
    public static double Combine(this Wiring w, IReadOnlyList<double> barrels) =>
        w == Wiring.Parallel ? barrels.Sum() : (barrels.Count == 0 ? 0.0 : barrels.Max());
}

public enum FillState
{
    Idle,           // لا يوجد وايت
    TruckDetected,  // تم التعرف على الوايت
    WaitingFlow,    // بانتظار بدء التدفق
    Counting,       // جارٍ حساب الماء
    StopWait,       // بانتظار انتهاء مهلة الدقيقة
    Paused,         // توقف مؤقت
    Completed,      // اكتملت التعبئة
}

public static class FillStateText
{
    public static string Ar(this FillState s) => s switch
    {
        FillState.Idle => "لا يوجد وايت",
        FillState.TruckDetected => "تم التعرف على الوايت",
        FillState.WaitingFlow => "بانتظار بدء التدفق",
        FillState.Counting => "جارٍ حساب الماء",
        FillState.StopWait => "بانتظار انتهاء مهلة الدقيقة",
        FillState.Paused => "توقف مؤقت",
        FillState.Completed => "اكتملت التعبئة",
        _ => "",
    };
}

public sealed record FillSnapshot(
    string SessionId, FillState State, IReadOnlyList<double> MeterBarrels, double TotalBarrels,
    long? FlowStartMs, long? LastFlowMs, long LastProcessedMs, double? GraceRemainingSec,
    bool FinalizedOnce, bool Closed, string Notes);

/// <summary>
/// جلسة تعبئة لوايت واحد، تُغذّى بعيّنات العدّادين بأوقات (ms). كل عدّاد مستقل، الإجمالي حسب Wiring،
/// وبعد السكون تنتظر مهلة (60ث مرصودة) فإن عاد التدفق استمرت الجلسة دون تصفير. آمنة للخيوط (قفل واحد).
/// </summary>
public sealed class FillSession
{
    private readonly object _lock = new();
    private readonly int _meters;
    private readonly Wiring _wiring;
    private readonly SwingParams _params;
    private readonly Action<FillSnapshot, SwingResult> _onFinalized;
    private readonly double[] _carried;
    private double? _flowStartT, _lastFlowT;
    private int _finalizedCount;
    private readonly List<string> _notes = new();
    private readonly SwingEngine _engine;
    private int _frames;

    public string Id { get; }
    public long EnterMs { get; }
    public bool TruckPresent { get; private set; } = true;
    public long? TruckLeftMs { get; private set; }
    public long LastProcessedMs { get; private set; } = long.MinValue;
    public bool Closed { get; private set; }

    public FillSession(string id, long enterMs, int meters, Wiring wiring, SwingParams @params,
                       double graceSec, Action<FillSnapshot, SwingResult> onFinalized)
    {
        Id = id; EnterMs = enterMs; _meters = meters; _wiring = wiring; _params = @params;
        _onFinalized = onFinalized;
        _carried = new double[meters];
        _engine = new SwingEngine(meters, @params with { ResolveGraceSec = graceSec }, OnResult);
    }

    public void SetTruckPresent(bool b) { lock (_lock) { if (!Closed) TruckPresent = b; } }
    public bool IsActive(int i) { lock (_lock) return _engine.IsActive(i); }
    public double CurrentDegrees(int i) { lock (_lock) return _engine.CurrentDegrees(i); }
    public double CumulativeDegrees(int i) { lock (_lock) return Degrees(i); }
    public bool HasFlowed { get { lock (_lock) return _finalizedCount > 0 || _engine.IsEpisodeOpen(); } }

    private double TSec(long ms) => (ms - EnterMs) / 1000.0;
    private long Ms(double t) => EnterMs + (long)(t * 1000.0);

    public void TruckLeft(long tMs) { lock (_lock) { TruckPresent = false; TruckLeftMs ??= tMs; } }

    /// <summary>إطار مسجَّل بوقت (ms). الإطارات المتأخرة/المكررة (≤ آخر معالَج) تُتجاهل.</summary>
    public void Feed(long tMs, IReadOnlyList<MeterSample?> samples)
    {
        lock (_lock)
        {
            if (Closed || tMs <= LastProcessedMs) return;
            LastProcessedMs = tMs;
            _frames++;
            _engine.Update(TSec(tMs), samples);
        }
    }

    /// <summary>انقطاع في التسجيل: لا يُحتسب توقفًا.</summary>
    public void MarkGap() { lock (_lock) { if (!Closed) _engine.MarkGap(); } }

    // تُستدعى من داخل Update (القفل ممسوك أصلًا — القفل في .NET قابل لإعادة الدخول)
    private void OnResult(SwingResult r)
    {
        foreach (var p in r.Parts) _carried[p.Meter] += p.Degrees;
        double st = r.Parts.Min(p => p.StartT), en = r.Parts.Max(p => p.EndT);
        _flowStartT = Math.Min(_flowStartT ?? st, st);
        _lastFlowT = Math.Max(_lastFlowT ?? en, en);
        if (r.Notes.Length > 0)
            foreach (var n in r.Notes.Split("؛ ")) if (!_notes.Contains(n)) _notes.Add(n);
        _finalizedCount++;
        _onFinalized(SnapshotUnlocked(), ResultUnlocked());
    }

    /// <summary>يُغلق نهائيًا (غادر الوايت وعولج التسجيل المتبقي).</summary>
    public void Finish()
    {
        lock (_lock)
        {
            if (Closed) return;
            _engine.Flush();
            Closed = true;
        }
    }

    private double Degrees(int i) => _carried[i] + _engine.EpisodeDegrees(i);

    private double? FirstMotion()
    {
        var e = _engine.FirstMotionT();
        if (_flowStartT != null && e != null) return Math.Min(_flowStartT.Value, e.Value);
        return _flowStartT ?? e;
    }

    private double? LastMotion()
    {
        var e = _engine.LastMotionT();
        if (_lastFlowT != null && e != null) return Math.Max(_lastFlowT.Value, e.Value);
        return _lastFlowT ?? e;
    }

    public FillSnapshot Snapshot() { lock (_lock) return SnapshotUnlocked(); }

    private FillSnapshot SnapshotUnlocked()
    {
        var bar = Enumerable.Range(0, _meters).Select(i => Degrees(i) / _params.DegPerBarrel).ToList();
        double? grace = _engine.GraceRemaining();
        bool active = Enumerable.Range(0, _meters).Any(_engine.IsActive);
        bool flowed = _finalizedCount > 0 || _engine.IsEpisodeOpen();
        FillState state =
            Closed ? (flowed ? FillState.Completed : FillState.Idle) :
            active ? FillState.Counting :
            grace != null ? FillState.StopWait :
            flowed ? (TruckPresent ? FillState.Paused : FillState.Completed) :
            _frames > 0 ? FillState.WaitingFlow : FillState.TruckDetected;
        var fm = FirstMotion(); var lm = LastMotion();
        return new FillSnapshot(Id, state, bar, _wiring.Combine(bar),
            fm.HasValue ? Ms(fm.Value) : null, lm.HasValue ? Ms(lm.Value) : null,
            LastProcessedMs, grace, _finalizedCount > 0, Closed, string.Join("؛ ", _notes));
    }

    public SwingResult Result() { lock (_lock) return ResultUnlocked(); }

    private SwingResult ResultUnlocked()
    {
        double st = FirstMotion() ?? 0.0;
        double en = LastMotion() ?? st;
        var parts = Enumerable.Range(0, _meters).Where(i => Degrees(i) > 0.0)
            .Select(i => new MeterPart(i, Degrees(i), st, en, _params.DegPerBarrel)).ToList();
        if (parts.Count == 0) parts.Add(new MeterPart(0, 0.0, st, en, _params.DegPerBarrel));
        var type = parts.Count >= 2 ? SwingType.Full : SwingType.Meter;
        double total = _wiring.Combine(Enumerable.Range(0, _meters).Select(i => Degrees(i) / _params.DegPerBarrel).ToList());
        return new SwingResult(type, parts, string.Join("؛ ", _notes), _wiring == Wiring.Series ? total : null);
    }

    /// <summary>وقت بدء التدفق ونهايته الفعلية (ms) لحفظهما في السجل.</summary>
    public (long StartMs, long EndMs) FlowWindowMs()
    {
        lock (_lock)
        {
            double st = FirstMotion() ?? TSec(EnterMs);
            return (Ms(st), Ms(LastMotion() ?? st));
        }
    }
}
