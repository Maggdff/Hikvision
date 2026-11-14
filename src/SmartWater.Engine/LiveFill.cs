using System.Globalization;
using SmartWater.Core;
using SmartWater.Data;

namespace SmartWater.Engine;

/// <param name="State">null = لا حالة محددة</param>
public sealed record FillStatus(string Label, FillState? State, string TruckName, IReadOnlyList<double> PerMeter,
                                double Total, double? GraceRemainingSec, string Note);

/// <summary>
/// تعبئة الوايت الحيّة (نقل لـ LiveFill.kt): جلسة واحدة لكل وايت تحت القصبة بقدر ما يبقى. تتغذّى بعيّنات العدّادين، وبقناة الوايتات
/// (كشف وجود/استبدال)، وتحفظ سجلًا واحدًا للجلسة (يُحدَّث دوريًا وعند الاعتماد). تُغلق الجلسة عند: مغادرة الوايت، تغيّر الوايت،
/// أو بقاء الوايت 10 دقائق بلا تدفق. تتعافى من انقطاع كشف الوايت (تعمل بلا انتظاره).
/// </summary>
public sealed class LiveFill
{
    public const double GraceSec = 60.0;
    public const long MaxPauseMs = 10 * 60_000L;
    public const long CkptMs = 15_000L;
    public const long StalePresenceMs = 120_000L;
    public const long RebaseMs = 20 * 60_000L;
    public const long DoneShowMs = 10 * 60_000L;
    public const long SampleMs = 2_000L;

    private readonly object _lock = new();
    private readonly Func<AppConfig> _config;
    private readonly Func<ISnapshotProvider?> _snapshots;
    private readonly TruckCoordinator _truckFlow;
    private readonly Action<string> _onSwingText;
    private readonly Action<FillStatus> _onStatus;
    private readonly Func<long> _clock;

    private FillSession? _session;
    private long _epId = -1;
    private bool _epTried;
    private long _baseWall = long.MinValue;
    private long _lastCkpt, _pausedSince, _lastPublish, _lastDoneAt;
    private FillStatus? _lastDone;

    private volatile bool _present, _resetBg, _running;
    private long _presenceOkAt, _presentSince;
    private SwapGuard _guard = new();
    private double _guardFrac = 0.35;
    private TruckPresenceDetector? _det;
    private string _zoneKey = "";
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public LiveFill(Func<AppConfig> config, Func<ISnapshotProvider?> snapshots, TruckCoordinator truckFlow,
                    Action<string> onSwingText, Action<FillStatus> onStatus, Func<long>? clock = null)
    {
        _config = config; _snapshots = snapshots; _truckFlow = truckFlow; _onSwingText = onSwingText; _onStatus = onStatus;
        _clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public bool Running => _running;

    public void Start()
    {
        if (_running) return;
        _running = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _loop = Task.Run(() => PresenceLoop(ct));
    }

    public void Stop()
    {
        _running = false;
        _cts?.Cancel();
        lock (_lock) { if (_session is { HasFlowed: true } s) Close(s, "أُوقفت المراقبة"); }
    }

    /// <summary>المستخدم يعتمد اللقطة الحالية للقناة 7 خلفيةً فارغة (يُنفَّذ عند اللقطة التالية).</summary>
    public void ResetBackground() => _resetBg = true;

    private bool PresenceActive(long now) => now - Volatile.Read(ref _presenceOkAt) < StalePresenceMs;
    private bool EffectivePresent(long now) => _present && PresenceActive(now);

    // ------------------------------------------------------------------ تغذية العدّادات
    private long WallOf(double t, long now)
    {
        if (_baseWall == long.MinValue) _baseWall = now - (long)(t * 1000.0);
        return _baseWall + (long)(t * 1000.0);
    }

    /// <summary>عيّنات كل العدّادات لإطار واحد؛ t بالثواني (ساعة رتيبة).</summary>
    public void Feed(double t, IReadOnlyList<MeterSample> samples)
    {
        lock (_lock)
        {
            long now = _clock();
            long ms = WallOf(t, now);
            var s = _session ?? (_session = NewSession(ms));
            s.SetTruckPresent(EffectivePresent(now));
            s.Feed(ms, samples);
            After(s, ms, now);
            if (now - _lastPublish >= 500) PublishLocked(now);
        }
    }

    public void MarkGap() { lock (_lock) _session?.MarkGap(); }

    public bool IsActive(int i) { lock (_lock) return _session?.IsActive(i) ?? false; }
    public double CurrentDegrees(int i) { lock (_lock) return _session?.CurrentDegrees(i) ?? 0.0; }
    public double CumulativeDegrees(int i) { lock (_lock) return _session?.CumulativeDegrees(i) ?? 0.0; }

    private FillSession NewSession(long ms)
    {
        var c = _config();
        FillSession? fs = null;
        fs = new FillSession($"L{ms}", ms, c.ActiveMeters.Count, c.Wiring == 1 ? Wiring.Series : Wiring.Parallel,
            c.Swing with { DegPerBarrel = c.DegPerBarrel }, GraceSec, (_, res) => { if (fs != null) Finalized(fs, res); });
        _epId = -1; _epTried = false; _pausedSince = 0; _lastCkpt = 0;
        return fs;
    }

    private void After(FillSession s, long ms, long now)
    {
        if (!s.HasFlowed) return;
        // بدأ التدفق: صورة الوايت من القناة 7 + تعرّف (أو سؤال) — مرة واحدة لكل جلسة
        if (_epId < 0 && !_epTried && _config().TruckChannel > 0 && _snapshots() != null)
        {
            _epTried = true;
            _epId = _truckFlow.BeginEpisode(null);
        }
        if (ms - _lastCkpt >= CkptMs)   // حفظ دوري للسجل أثناء التعبئة
        {
            _lastCkpt = ms;
            var (st, en) = s.FlowWindowMs();
            _truckFlow.RecordSession(_epId, s.Id, s.Result(), st, en);
        }
        var snap = s.Snapshot();
        if (snap.State == FillState.Paused)
        {
            if (_pausedSince == 0) _pausedSince = ms;
            if (ms - _pausedSince > MaxPauseMs) Close(s, "الوايت لا يزال تحت القصبة لكن لا تدفق منذ 10 دقائق");
        }
        else if (snap.State == FillState.Completed && snap.FinalizedOnce) Close(s, "غادر الوايت");
        else _pausedSince = 0;
    }

    /// <summary>اعتماد/تحديث سجل الجلسة (بعد المهلة أو عند الإغلاق). يُحدَّث السجل نفسه عند الاستئناف.</summary>
    private void Finalized(FillSession s, SwingResult res)
    {
        var (st, en) = s.FlowWindowMs();
        var rec = _truckFlow.RecordSession(_epId, s.Id, res, st, en);
        _lastCkpt = en;
        _onSwingText(Describe(rec, res));
        EngineLog.Info($"اعتُمدت الجلسة {s.Id} → سجل #{rec.Seq}: مقاس {res.TotalBarrels.ToString("0.000", CultureInfo.InvariantCulture)} برميل");
    }

    private string Describe(SwingRecord rec, SwingResult r)
    {
        var names = _config().ActiveMeters.Select(m => m.Name).ToList();
        string F2(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
        var parts = string.Join("\n", r.Parts.Select(m =>
            $"• {(m.Meter < names.Count ? names[m.Meter] : $"عداد {m.Meter + 1}")}: {F2(m.Barrels)} برميل ({m.Degrees.ToString("0", CultureInfo.InvariantCulture)}°)"));
        string truck =
            rec.TruckName.Length > 0 ? $"\nالوايت: {rec.TruckName} — المسجَّل {F2(rec.Total)} برميل (الكمية المدخلة)" :
            rec.Photo.Length > 0 ? "\nالوايت: لم يُحدَّد بعد (تُستبدل الكمية المقاسة بكمية الوايت عند اختياره)" : "";
        string kind = rec.TruckName.Length > 0 ? "زفة كاملة" : rec.TypeAr;
        return $"زفة رقم {rec.Seq} — {kind}\n{parts}\nمقاس: {F2(r.TotalBarrels)} برميل{truck}" +
               (r.Notes.Length > 0 ? $"\n⚠ {r.Notes}" : "");
    }

    /// <summary>الماء متوقف (لا جريان حاليًا) وللجلسة تدفق سابق: أغلقها فورًا (انتهت تعبئة وايتها). أثناء الجريان لا تُغلق.</summary>
    private void CloseIfQuiet(string why)
    {
        var s = _session;
        if (s == null) return;
        if (s.HasFlowed && s.Snapshot().State != FillState.Counting) Close(s, why);
    }

    private void Close(FillSession s, string why)
    {
        s.Finish();   // يعتمد ما بقي من حركة/مهلة بنهاية عند آخر حركة فعلية
        var snap = s.Snapshot();
        _truckFlow.CloseEpisode(_epId, true);
        var name = _truckFlow.EpisodeTruck(_epId)?.Name ?? "";
        _lastDone = new FillStatus(FillState.Completed.Ar(), FillState.Completed, name, snap.MeterBarrels, snap.TotalBarrels, null, why);
        _lastDoneAt = _clock();
        EngineLog.Info($"أُغلقت الجلسة {s.Id}: {snap.TotalBarrels.ToString("0.000", CultureInfo.InvariantCulture)} برميل مقاس — {why}");
        _session = null; _epId = -1; _epTried = false; _pausedSince = 0; _lastCkpt = 0;
        PublishLocked(_clock());
    }

    // ------------------------------------------------------------------ الحالة
    public FillStatus Status() { lock (_lock) return StatusLocked(_clock()); }

    private FillStatus StatusLocked(long now)
    {
        var s = _session;
        var c = _config();
        int n = c.ActiveMeters.Count;
        bool present = EffectivePresent(now);
        if (s == null || !s.HasFlowed)
        {
            var d = _lastDone;
            if (d != null && now - _lastDoneAt < DoneShowMs && !present) return d;
            string label = present ? "وايت تحت القصبة — بانتظار بدء التدفق"
                : PresenceActive(now) ? "لا يوجد وايت — بانتظار بدء التدفق"
                : FillState.WaitingFlow.Ar();
            string note = c.TruckChannel > 0 && !PresenceActive(now) && _running
                ? $"لا لقطات من القناة {c.TruckChannel} — كشف الوايت متوقف (يعمل العدّ بلا انتظار الوايت)" : "";
            return new FillStatus(label, FillState.WaitingFlow, "", Enumerable.Repeat(0.0, n).ToList(), 0.0, null, note);
        }
        var snap = s.Snapshot();
        var name = _truckFlow.EpisodeTruck(_epId)?.Name ?? "";
        string stNote = snap.State switch
        {
            FillState.Paused => "الوايت لا يزال تحت القصبة — يُستأنف العدّ في السجل نفسه عند عودة الماء",
            FillState.StopWait => "إن عاد الماء قبل انتهاء المهلة يستمر الحساب دون تصفير",
            _ => "",
        };
        return new FillStatus(snap.State.Ar(), snap.State, name, snap.MeterBarrels, snap.TotalBarrels, snap.GraceRemainingSec,
            string.Join(" • ", new[] { stNote, snap.Notes }.Where(x => x.Length > 0)));
    }

    private void PublishLocked(long now) { _lastPublish = now; _onStatus(StatusLocked(now)); }

    // ------------------------------------------------------------------ كشف الوايت (قناة الوايتات)
    private async Task PresenceLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            long t0 = Environment.TickCount64;
            try
            {
                var c = _config();
                var snap = _snapshots();
                if (c.TruckChannel > 0 && snap != null)
                {
                    var jpeg = await snap.SnapshotJpegAsync(c.TruckChannel, ct).ConfigureAwait(false);
                    if (jpeg != null)
                    {
                        using var m = ImageIo.DecodeScaled(jpeg, 640);
                        if (m != null)
                        {
                            var (g, w, h) = ImageConv.ToGrayBytes(m);
                            ProcessPresence(_clock(), g, w, h, jpeg);
                        }
                    }
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e) { EngineLog.Error("خيط كشف الوايت", e); }
            long rest = SampleMs - (Environment.TickCount64 - t0);
            if (rest > 0) { try { await Task.Delay((int)rest, ct).ConfigureAwait(false); } catch (OperationCanceledException) { return; } }
        }
    }

    /// <summary>
    /// معالجة لقطة واحدة من قناة الوايتات (رمادي): دخول/خروج/استبدال، حارس تغيّر الوايت، وإعادة ضبط الخلفية العالقة.
    /// عامة ليُختبر السيناريو كاملًا بلقطات مصنوعة دون كاميرا.
    /// </summary>
    public void ProcessPresence(long now, byte[] gray, int w, int h, byte[]? jpeg)
    {
        var c = _config();
        if (_det == null || c.TruckZone != _zoneKey)
        {
            _zoneKey = c.TruckZone;
            _det = new TruckPresenceDetector(new PresenceParams { Zone = ParseZone(_zoneKey) });
        }
        var det = _det;
        Volatile.Write(ref _presenceOkAt, now);
        if (_resetBg)
        {
            _resetBg = false;
            det.SetBackground(gray, w, h);
            EngineLog.Info($"اعتُمدت لقطة القناة {c.TruckChannel} خلفيةً فارغة");
            lock (_lock) _present = false;
        }
        var ev = det.Update(now, gray, w, h);
        switch (ev)
        {
            case PresenceEvent.Entered:
                lock (_lock)
                {
                    _present = true; _presentSince = now; EngineLog.Info("وايت تحت القصبة");
                    // دخل وايت والماء متوقف وللجلسة السابقة تدفق: هو وايت جديد (لا تنتظر مهلة الدقيقة)
                    CloseIfQuiet("دخل وايت جديد"); PublishLocked(now);
                }
                break;
            case PresenceEvent.Left:
                lock (_lock) { _present = false; EngineLog.Info("غادر الوايت"); CloseIfQuiet("غادر الوايت"); PublishLocked(now); }
                break;
            case PresenceEvent.Replaced:
                lock (_lock) { EngineLog.Info("استُبدل الوايت"); CloseIfQuiet("استُبدل الوايت"); }
                break;
        }
        lock (_lock)
        {
            // مرجع الوايت يُثبَّت أثناء جريان الماء (واقف تحت القصبة)، وبعد توقفه أي تغيّر كبير في المنطقة = وايت آخر
            if (_guardFrac != c.SwapFrac) { _guardFrac = c.SwapFrac; _guard = new SwapGuard(c.SwapFrac); }
            var s = _session;
            if (s != null && s.HasFlowed)
            {
                if (s.Snapshot().State == FillState.Counting) { det.SetReference(); _guard.Reset(); }
                else if (_guard.Update(det.RefChange))
                {
                    EngineLog.Info($"تغيّر منطقة الوايت {(det.RefChange * 100).ToString("0", CultureInfo.InvariantCulture)}% عن مرجعه: وايت جديد");
                    Close(s, "تغيّر الوايت"); _guard.Reset(); det.ClearReference();
                }
            }
            else { _guard.Reset(); det.ClearReference(); }
        }
        lock (_lock)
        {
            // «موجود» عالقة بلا أي تدفق (خلفية خاطئة عند التشغيل مثلًا): أعد ضبط الخلفية
            if (_present && now - _presentSince > RebaseMs && _session?.HasFlowed != true)
            {
                det.RebaseToLast(); _present = false;
                EngineLog.Info($"أُعيد ضبط خلفية القناة {c.TruckChannel} (حالة «موجود» بلا تدفق 20 دقيقة)");
            }
        }
    }

    private static double[]? ParseZone(string s)
    {
        var v = s.Split(',').Select(x => double.TryParse(x.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (double?)d : null)
                 .Where(x => x != null).Select(x => x!.Value).ToArray();
        if (v.Length != 4 || v[2] <= 0 || v[3] <= 0) return null;
        return new[] { v[0] / 100, v[1] / 100, v[2] / 100, v[3] / 100 };
    }
}
