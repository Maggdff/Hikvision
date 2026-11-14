using System.Globalization;
using OpenCvSharp;
using SmartWater.Core;
using SmartWater.Data;

namespace SmartWater.Engine;

public sealed record MeterState(string Name, bool Valid, bool Active, double TotalDeg, double RawAngle, double CurrentDeg,
                                double Contrast, double ShiftX, double ShiftY, double EpisodeDeg);

public sealed record UiState(bool Connected, bool Running, int FrameW, int FrameH, IReadOnlyList<MeterState> Meters, double Fps);

/// <summary>
/// المحرك المشترك بين الواجهة والخدمة: لقطات الكاميرا → Pipeline (تتبّع العدّادات) → LiveFill (جلسة التعبئة) → سجل الزفات.
/// نقل لـ MonitorEngine.kt. يعمل بحلقة لا تنتهي مع إعادة اتصال تلقائية (انقطاع البث لا يُحتسب توقفًا للعدّاد).
/// </summary>
public sealed class MonitorEngine : IDisposable
{
    private readonly object _sync = new();
    private readonly object _stillLock = new();
    private readonly Func<AppConfig, IsapiSnapshotClient> _clientFactory;
    private AppConfig _cfg;

    public ConfigStore Store { get; }
    public SwingDb Swings { get; }
    public TruckDb Trucks { get; }
    public TruckCoordinator TruckFlow { get; }
    public AppConfig Config => _cfg;

    public event Action<UiState>? StateChanged;
    public event Action<string>? Swing;
    public event Action<string>? Status;
    public event Action<FillStatus>? Fill;

    private Pipeline? _pipeline;
    private LiveFill? _fill;
    private ISnapshotProvider? _snapshots;
    private IsapiSnapshotClient? _client;
    private CancellationTokenSource? _cts;
    private Task? _runTask;

    private volatile bool _running, _connected;
    private long _lastUi, _fpsT, _lastFrameTick, _roiWarnKey, _sizeWarnKey;
    private int _fpsCount;
    private double _fps;
    private Mat? _latest, _still;

    public bool Running => _running;
    public bool Connected => _connected;
    /// <summary>ثوانٍ منذ آخر إطار معالَج (للمراقبة والـ Heartbeat)؛ كبيرة جدًا إن لم يصل إطار بعد.</summary>
    public double FrameAgeSec => _lastFrameTick == 0 ? double.MaxValue : (Environment.TickCount64 - _lastFrameTick) / 1000.0;
    public IReadOnlyList<MeterTracker> Trackers => _pipeline?.Trackers ?? Array.Empty<MeterTracker>();

    public MonitorEngine(string dataDir, Func<AppConfig, IsapiSnapshotClient>? clientFactory = null)
    {
        Directory.CreateDirectory(dataDir);
        Store = new ConfigStore(dataDir);
        Swings = new SwingDb(Path.Combine(dataDir, "swings.db"));
        Trucks = new TruckDb(Path.Combine(dataDir, "trucks.db"));
        _cfg = Store.Load();
        _clientFactory = clientFactory ?? (c => new IsapiSnapshotClient(c.Ip, c.HttpPort, c.User, c.Pass));
        TruckFlow = new TruckCoordinator(Trucks, Swings, () => _cfg, () => _snapshots, Path.Combine(dataDir, "truck_snaps"));
        TruckFlow.Info += m => Status?.Invoke(m);
        Task.Run(TruckFlow.Cleanup);
    }

    // ---------- التشغيل ----------
    public void Start()
    {
        Stop();
        AppConfig c;
        lock (_sync)
        {
            _cfg = Store.Load();
            c = _cfg;
            var meters = c.ActiveMeters;
            var refs = meters.Select((_, i) => Store.LoadReference(i)).ToList();
            _fill = new LiveFill(() => _cfg, () => _snapshots, TruckFlow, t => Swing?.Invoke(t), st => Fill?.Invoke(st));
            _pipeline = new Pipeline(meters, refs, (t, s) => _fill?.Feed(t, s));
            _fill.Start();
            _client = _clientFactory(c);
            _snapshots = new IsapiSnapshotProvider(_client, c.StreamType);
            var src = new SnapshotFrameSource(_client, c.Channel, c.StreamType, fps: 4.0);
            src.OnFrame += OnFrame;
            src.OnStatus += s => Status?.Invoke(s);
            src.OnDisconnected += OnDisconnected;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            _running = true; _connected = false; _lastFrameTick = 0;
            _runTask = Task.Run(() => src.RunAsync(ct));
        }
        EngineLog.Info($"طلب الاتصال: {c.Ip}:{c.HttpPort} قناة {c.Channel} — {c.ActiveMeters.Count} عدّاد");
        Status?.Invoke($"جارٍ الاتصال بـ {c.Ip}:{c.HttpPort} ، القناة {c.Channel} ...");
    }

    public void Stop()
    {
        LiveFill? f; CancellationTokenSource? cts; Task? run; IsapiSnapshotClient? client; bool was;
        lock (_sync)
        {
            f = _fill; _fill = null; cts = _cts; _cts = null; run = _runTask; _runTask = null;
            client = _client; _client = null; _snapshots = null; was = _running;
        }
        f?.Stop();                                  // يعتمد ويغلق أي جلسة جارية
        cts?.Cancel();
        try { run?.Wait(TimeSpan.FromSeconds(5)); } catch (AggregateException) { }
        client?.Dispose();
        if (was) { _running = false; _connected = false; Status?.Invoke("متوقف"); }
    }

    /// <summary>لقطة من قناة الوايتات لواجهة رسم المنطقة. (jpeg، رسالة خطأ).</summary>
    public async Task<(byte[]? Jpeg, string Error)> GrabTruckSnapshotAsync(CancellationToken ct = default)
    {
        var src = _snapshots; int ch = _cfg.TruckChannel;
        if (src == null || ch <= 0) return (null, "اتصل بالكاميرا أولًا (وقناة الوايتات غير صفر)");
        var jpg = await src.SnapshotJpegAsync(ch, ct).ConfigureAwait(false);
        return jpg == null ? (null, $"تعذّر التقاط صورة القناة {ch}") : (jpg, "");
    }

    /// <summary>يعتمد لقطة قناة الوايتات التالية خلفيةً فارغة لكاشف الوجود (والمنطقة خالية).</summary>
    public bool ResetPresenceBackground() { var f = _fill; if (f == null) return false; f.ResetBackground(); return true; }

    public FillStatus? CurrentFill() => _fill?.Status();

    // ---------- استقبال الإطارات ----------
    private void OnFrame(Mat gray, double tSec)
    {
        try { Process(gray, tSec); }
        catch (Exception e) { EngineLog.Error("خطأ معالجة الإطار", e); Status?.Invoke($"خطأ معالجة: {e.Message}"); }
    }

    private void OnDisconnected()
    {
        _connected = false;
        _fill?.MarkGap();   // انقطاع البث ليس توقف عدّاد
        Status?.Invoke("انقطع البث — لا يُحتسب توقفًا للعدّادين");
        Publish();
    }

    private void Process(Mat gray, double t)
    {
        var p = _pipeline;
        if (p == null) return;
        _connected = true;
        int w = gray.Cols, h = gray.Rows;
        // الإعدادات الافتراضية لإطار 2560×1944؛ إن كانت حلقة أي عدّاد خارج حدود الإطار الفعلي لا نعالج بل نطلب المعايرة
        var outside = new List<int>();
        for (int i = 0; i < p.Meters.Count; i++)
        {
            var m = p.Meters[i]; double r = 1.02 * m.Radius;
            if (m.Cx - r < 0 || m.Cy - r < 0 || m.Cx + r > w || m.Cy + r > h) outside.Add(i);
        }
        var cc = _cfg;
        long sizeKey = (long)w * 100000 + h;
        if (cc.CalW > 0 && (cc.CalW != w || cc.CalH != h) && _sizeWarnKey != sizeKey)
        {
            _sizeWarnKey = sizeKey;
            var msg = $"تنبيه: عُويرت المواضع على إطار {cc.CalW}×{cc.CalH} والبث الآن {w}×{h} — أعد المعايرة إن اختلفت الصورة.";
            EngineLog.Info(msg); Status?.Invoke(msg);
        }
        if (outside.Count == 0) { _roiWarnKey = 0; p.Process(gray, t); }
        else if (_roiWarnKey != sizeKey)
        {
            _roiWarnKey = sizeKey;
            var msg = $"المعايرة خارج حدود الصورة {w}×{h} (العدّاد {string.Join("، ", outside.Select(i => i + 1))}). افتح المعايرة وحدّد العدّادين على صورة الكاميرا.";
            EngineLog.Info(msg); Status?.Invoke(msg);
        }
        _lastFrameTick = Environment.TickCount64;
        long now = Environment.TickCount64;
        if (now - _lastUi > 200)
        {
            _lastUi = now;
            lock (_stillLock) { _latest?.Dispose(); _latest = gray.Clone(); }
            Publish(w, h);
        }
        _fpsCount++;
        if (_fpsT == 0) _fpsT = now;
        if (now - _fpsT >= 2000) { _fps = _fpsCount * 1000.0 / (now - _fpsT); _fpsCount = 0; _fpsT = now; }
    }

    private void Publish(int w = 0, int h = 0)
    {
        var p = _pipeline;
        if (p == null) return;
        var smp = p.Samples; var f = _fill;
        var ms = Enumerable.Range(0, p.Meters.Count).Select(i =>
        {
            var s = i < smp.Count ? smp[i] : null;
            return new MeterState(p.Meters[i].Name, s?.Valid ?? false, f?.IsActive(i) ?? false, p.Trackers[i].TotalDegrees,
                s?.RawAngleDeg ?? 0.0, f?.CurrentDegrees(i) ?? 0.0, s?.Contrast ?? 0.0, s?.ShiftX ?? 0.0, s?.ShiftY ?? 0.0,
                f?.CumulativeDegrees(i) ?? 0.0);
        }).ToList();
        StateChanged?.Invoke(new UiState(_connected, _running, w, h, ms, _fps));
    }

    // ---------- المعايرة ----------
    public int StillWidth { get { lock (_stillLock) return _still?.Cols ?? 0; } }
    public int StillHeight { get { lock (_stillLock) return _still?.Rows ?? 0; } }

    public bool SetStillFromLatest()
    {
        lock (_stillLock)
        {
            if (_latest == null) return false;
            _still?.Dispose(); _still = _latest.Clone();
            return true;
        }
    }

    private static byte[]? EncodeScaled(Mat m, int maxDim)
    {
        double sc = Math.Min(1.0, maxDim / (double)Math.Max(m.Cols, m.Rows));
        using var outM = new Mat();
        Cv2.Resize(m, outM, new Size(Math.Max(1, (int)(m.Cols * sc)), Math.Max(1, (int)(m.Rows * sc))), 0, 0, InterpolationFlags.Area);
        Cv2.ImEncode(".jpg", outM, out var buf);
        return buf;
    }

    /// <summary>معاينة آخر إطار حي (JPEG) بتصغير يناسب العرض.</summary>
    public byte[]? LatestJpeg(int maxDim) { lock (_stillLock) return _latest == null ? null : EncodeScaled(_latest, maxDim); }

    /// <summary>جزء من لقطة المعايرة (JPEG) مع حدوده الفعلية [x0,y0,x1,y1] بإحداثيات الإطار الكامل.</summary>
    public (byte[] Jpeg, float[] Rect)? StillCropJpeg(double x0, double y0, double x1, double y1, int maxDim)
    {
        lock (_stillLock)
        {
            var m = _still;
            if (m == null) return null;
            int rx0 = Math.Max(0, K(x0)), ry0 = Math.Max(0, K(y0)), rx1 = Math.Min(m.Cols, K(x1)), ry1 = Math.Min(m.Rows, K(y1));
            if (rx1 - rx0 < 8 || ry1 - ry0 < 8) return null;
            using var sub = new Mat(m, new Rect(rx0, ry0, rx1 - rx0, ry1 - ry0));
            return (EncodeScaled(sub, maxDim)!, new[] { (float)rx0, ry0, rx1, ry1 });
        }
    }

    private static int K(double v) => (int)Math.Floor(v + 0.5);

    /// <summary>يحفظ المعايرة ومرجع كل عدّاد (ROI) من لقطة المعايرة. يعيد null عند النجاح، وإلا نص الخطأ.</summary>
    public string? SaveCalibration(AppConfig newCfg)
    {
        lock (_stillLock)
        {
            var m = _still;
            if (m == null) return "التقط لقطة أولًا (زر «التقاط من البث»)";
            int w = m.Cols, h = m.Rows;
            var outside = new List<int>();
            var act = newCfg.ActiveMeters;
            for (int i = 0; i < act.Count; i++)
            {
                double r = 1.02 * act[i].Radius;
                if (act[i].Cx - r < 0 || act[i].Cy - r < 0 || act[i].Cx + r > w || act[i].Cy + r > h) outside.Add(i + 1);
            }
            if (outside.Count > 0) return $"العدّاد {string.Join("، ", outside)} خارج حدود الصورة {w}×{h} — حدّد موضعه على الصورة ثم احفظ";
            try
            {
                var saved = newCfg with { CalW = w, CalH = h };
                Store.Save(saved);
                for (int i = 0; i < act.Count; i++) Store.SaveReference(i, Roi.Extract(m, act[i]));
                _cfg = saved;
                EngineLog.Info($"حُفظت المعايرة على إطار {w}×{h}: " + string.Join(", ",
                    saved.Meters.Select(x => string.Format(CultureInfo.InvariantCulture, "{0}({1:0},{2:0},r={3:0})", x.Name, x.Cx, x.Cy, x.Radius))));
                return null;
            }
            catch (Exception e) { EngineLog.Error("فشل حفظ المعايرة", e); return $"فشل حفظ المعايرة: {e.Message}"; }
        }
    }

    public void Dispose()
    {
        Stop();
        lock (_stillLock) { _latest?.Dispose(); _still?.Dispose(); _latest = _still = null; }
        Swings.Dispose(); Trucks.Dispose();
    }
}
