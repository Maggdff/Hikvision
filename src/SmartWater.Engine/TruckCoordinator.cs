using System.Globalization;
using OpenCvSharp;
using SmartWater.Core;
using SmartWater.Data;

namespace SmartWater.Engine;

/// <summary>ما تعرضه الواجهة للسؤال «أي وايت هذا؟».</summary>
public sealed record TruckPrompt(long EpisodeId, string? PhotoPath, IReadOnlyList<Truck> Trucks, string Note);

/// <summary>
/// سير عمل الوايت لكل «حلقة» (زبون): بدء حركة → لقطة من قناة الوايتات → محاولة تعرّف من الشكل واللون → إن لم نتأكد نسأل المستخدم →
/// تُسجَّل الزفة بكمية الوايت المدخلة (زفة كاملة) وتُحفظ الصورة، وتُضاف للتعلّم عند اختيار المستخدم. نقل لـ TruckCoordinator.kt.
/// لا يُفقَد شيء إن تأخر الجواب: الزفة تُسجَّل فورًا بالكمية المقاسة ثم تُحدَّث عند اختيار الوايت. كل عملية مرتبطة بحلقة واحدة فلا تتكرر.
/// </summary>
public sealed class TruckCoordinator
{
    private sealed class Episode
    {
        public long Id { get; }
        public Episode(long id) => Id = id;
        public string? PhotoPath; public float[]? Feat; public Truck? Truck;
        public long RecordId = -1;
        public bool Ready, Resulted, Dismissed, Cancelled;
        public IReadOnlyList<long> Ranking = Array.Empty<long>();
        public string Note = "";
        public byte[]? PreJpeg;
        public bool Answered => Truck != null || Dismissed || Cancelled;
    }

    private readonly TruckDb _trucks;
    private readonly SwingDb _swings;
    private readonly Func<AppConfig> _config;
    private readonly Func<ISnapshotProvider?> _snapshotSource;
    private readonly string _snapDir;
    private readonly object _lock = new();
    private readonly List<Episode> _episodes = new();
    private readonly List<Task> _captures = new();
    private long _nextId = 1;

    public event Action<TruckPrompt>? Prompt;
    public event Action<long>? PromptClosed;
    public event Action<string>? Info;

    public TruckCoordinator(TruckDb trucks, SwingDb swings, Func<AppConfig> config, Func<ISnapshotProvider?> snapshotSource, string snapDir)
    {
        _trucks = trucks; _swings = swings; _config = config; _snapshotSource = snapshotSource; _snapDir = snapDir;
        Directory.CreateDirectory(snapDir);
    }

    private void Say(string m) { EngineLog.Info(m); Info?.Invoke(m); }
    private static string Fmt(double d) => d == Math.Floor(d) ? ((long)d).ToString(CultureInfo.InvariantCulture) : d.ToString("0.00", CultureInfo.InvariantCulture);

    private Episode? Find(long id) { lock (_lock) return _episodes.FirstOrDefault(e => e.Id == id); }

    /// <summary>للاختبارات والإيقاف الآمن: ينتظر انتهاء كل عمليات الالتقاط الجارية.</summary>
    public Task WaitForCapturesAsync() { lock (_lock) return Task.WhenAll(_captures.ToArray()); }

    // ---------------- الحلقة مرتبطة بجلسة تعبئة ----------------
    /// <summary>بدأت تعبئة: التقط صورة الوايت (أو استعمل jpeg الجاهزة) وتعرّف عليه. يعيد رقم الحلقة.</summary>
    public long BeginEpisode(byte[]? jpeg)
    {
        var cfg = _config();
        Episode ep;
        lock (_lock) { ep = new Episode(_nextId++) { PreJpeg = jpeg }; _episodes.Add(ep); }
        EngineLog.Info($"دخل وايت: بدء التعرّف (حلقة {ep.Id})");
        var t = Task.Run(() => CaptureAsync(ep, cfg));
        lock (_lock) _captures.Add(t);
        return ep.Id;
    }

    public Truck? EpisodeTruck(long episodeId) { var ep = Find(episodeId); if (ep == null) return null; lock (ep) return ep.Truck; }

    /// <summary>اعتماد/تحديث سجل جلسة التعبئة (سجل واحد لكل جلسة). إن عُرف الوايت: الكمية = كميته المدخلة وتبقى المقاسة منفصلة.</summary>
    public SwingRecord RecordSession(long episodeId, string sessionId, SwingResult r, long startMs, long endMs)
    {
        var ep = Find(episodeId);
        if (ep == null) return _swings.Insert(r, startMs, endMs, sessionId: sessionId);
        lock (ep)
        {
            ep.Resulted = true;
            var rec = _swings.Insert(r, startMs, endMs, ep.Truck, ep.PhotoPath, sessionId);
            ep.RecordId = rec.Id;
            if (ep.Answered) lock (_lock) _episodes.Remove(ep);
            return rec;
        }
    }

    /// <summary>أُغلقت الجلسة: إن لم يتدفق ماء تُلغى الحلقة (لا سجل ولا سؤال)؛ وإلا يبقى السؤال المعلّق حتى تجيب.</summary>
    public void CloseEpisode(long episodeId, bool hadFlow)
    {
        if (hadFlow) return;
        var ep = Find(episodeId);
        if (ep == null) return;
        string? photo;
        lock (ep) { ep.Cancelled = true; photo = ep.Truck == null ? ep.PhotoPath : null; }
        lock (_lock) _episodes.Remove(ep);
        if (photo != null) { try { File.Delete(photo); } catch { } }
        PromptClosed?.Invoke(ep.Id);
    }

    // ---------------- اللقطة والتعرّف ----------------
    private async Task CaptureAsync(Episode ep, AppConfig cfg)
    {
        try
        {
            byte[]? jpeg = ep.PreJpeg;
            if (jpeg == null && _snapshotSource() is { } src) jpeg = await src.SnapshotJpegAsync(cfg.TruckChannel).ConfigureAwait(false);
            ep.PreJpeg = null;
            if (ep.Cancelled) return;
            if (jpeg == null)
            {
                ep.Note = $"تعذّر التقاط صورة القناة {cfg.TruckChannel}. اختر الوايت يدويًا.";
                Say($"تعذّر التقاط صورة الوايت من القناة {cfg.TruckChannel}");
                FinishCapture(ep); return;
            }
            using var bmp = ImageIo.DecodeScaled(jpeg, 960);
            if (bmp == null) { ep.Note = "تعذّر قراءة الصورة الملتقطة. اختر الوايت يدويًا."; FinishCapture(ep); return; }
            var path = Path.Combine(_snapDir, $"snap_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{ep.Id}.jpg");
            try { Cv2.ImWrite(path, bmp, new ImageEncodingParam(ImwriteFlags.JpegQuality, 88)); ep.PhotoPath = path; }
            catch (Exception e) { EngineLog.Error("تعذّر حفظ صورة الوايت", e); }
            var feat = TruckFeatures.Describe(ImageConv.ToRgbImage(bmp));
            ep.Feat = feat;

            var res = TruckMatcher.Match(feat, _trucks.Samples());
            ep.Ranking = res.Ranking.Select(x => x.TruckId).ToList();
            if (_config().AutoRecognize && res.TruckId is long tid)
            {
                var t = _trucks.Get(tid);
                if (t != null)
                {
                    EngineLog.Info($"تعرّف تلقائي: {t.Name} ({res.Best:0.00} مقابل {res.Second:0.00})");
                    if (Apply(ep, t, learn: false))
                    {
                        Say($"تعرّفت على الوايت: {t.Name} — {Fmt(t.Capacity)} برميل (يمكن تغييره من سجل الزفات)");
                        FinishCapture(ep); return;
                    }
                }
            }
            ep.Note = res.Reason.Length == 0 || res.Ranking.Count == 0 ? "" : $"لم أتأكد من الوايت — {res.Reason}";
            FinishCapture(ep);
        }
        catch (Exception e)
        {
            EngineLog.Error("خطأ في سير الوايت", e);
            ep.Note = "حدث خطأ أثناء التعرّف. اختر الوايت يدويًا.";
            FinishCapture(ep);
        }
    }

    private void FinishCapture(Episode ep)
    {
        TruckPrompt p;
        lock (ep)
        {
            ep.Ready = true;
            if (ep.Answered || ep.Cancelled) return;
            p = MakePrompt(ep);
        }
        Prompt?.Invoke(p);
    }

    private TruckPrompt MakePrompt(Episode ep) => new(ep.Id, ep.PhotoPath, Ordered(ep.Ranking), ep.Note);

    /// <summary>الوايتات المحفوظة بترتيب التشابه (إن وُجد) ثم الباقي أبجديًا.</summary>
    public IReadOnlyList<Truck> Ordered(IReadOnlyList<long> ranking)
    {
        var all = _trucks.List();
        var byId = all.ToDictionary(t => t.Id);
        var first = ranking.Where(byId.ContainsKey).Select(i => byId[i]).ToList();
        var set = ranking.ToHashSet();
        return first.Concat(all.Where(t => !set.Contains(t.Id))).ToList();
    }

    /// <summary>أسئلة لم تُجَب بعد (تُعاد للواجهة عند إعادة فتحها).</summary>
    public IReadOnlyList<TruckPrompt> OpenPrompts()
    {
        List<Episode> eps; lock (_lock) eps = _episodes.Where(e => e.Ready && !e.Answered).ToList();
        return eps.Select(MakePrompt).ToList();
    }

    // ---------------- جواب المستخدم ----------------
    /// <summary>اختار المستخدم وايتًا لهذه الحلقة (آمنة عند التكرار: الجواب الأول هو المعتمد).</summary>
    public bool Choose(long episodeId, long truckId)
    {
        var ep = Find(episodeId);
        if (ep == null) return false;
        var t = _trucks.Get(truckId);
        if (t == null) return false;
        if (!Apply(ep, t, learn: true)) return false;
        PromptClosed?.Invoke(ep.Id);
        return true;
    }

    /// <summary>«لاحقًا»: تبقى الزفة بلا وايت ويمكن ربطها من السجل.</summary>
    public void Dismiss(long episodeId)
    {
        var ep = Find(episodeId);
        if (ep == null) return;
        lock (ep)
        {
            if (ep.Truck != null) return;
            ep.Dismissed = true;
            if (ep.Resulted) lock (_lock) _episodes.Remove(ep);
        }
        PromptClosed?.Invoke(ep.Id);
    }

    private bool Apply(Episode ep, Truck t, bool learn)
    {
        lock (ep)
        {
            if (ep.Truck != null || ep.Cancelled) return false;
            ep.Truck = t;
            if (learn && ep.Feat is { } f && ep.PhotoPath is { } p) _trucks.AddPhoto(t.Id, p, f);
            if (ep.Resulted)
            {
                if (ep.RecordId > 0) _swings.AssignTruck(ep.RecordId, t, ep.PhotoPath);
                lock (_lock) _episodes.Remove(ep);
            }
            return true;   // لم تصل نتيجة الزفة بعد: يُطبَّق الوايت عند RecordSession
        }
    }

    // ---------------- ربط/تصحيح سجل موجود ----------------
    public Task<string> AssignRecordAsync(long recordId, long truckId) => Task.Run(() =>
    {
        try
        {
            var rec = _swings.ById(recordId);
            var t = _trucks.Get(truckId);
            if (rec == null) return "السجل غير موجود";
            if (t == null) return "الوايت غير موجود";
            string? photo = rec.Photo.Length > 0 && File.Exists(rec.Photo) ? rec.Photo : null;
            if (photo != null) LearnFrom(photo, t);
            _swings.AssignTruck(recordId, t, photo);
            CloseEpisodeOf(recordId, t);
            return $"رُبطت الزفة بالوايت «{t.Name}» — {Fmt(t.Capacity)} برميل";
        }
        catch (Exception e) { EngineLog.Error("فشل ربط السجل", e); return $"تعذّر الربط: {e.Message}"; }
    });

    private void CloseEpisodeOf(long recordId, Truck t)
    {
        Episode? ep; lock (_lock) ep = _episodes.FirstOrDefault(e => Interlocked.Read(ref e.RecordId) == recordId);
        if (ep == null) return;
        lock (ep) { if (ep.Truck == null) ep.Truck = t; }
        lock (_lock) _episodes.Remove(ep);
        PromptClosed?.Invoke(ep.Id);
    }

    private void LearnFrom(string path, Truck t)
    {
        using var bmp = ImageIo.DecodeScaled(File.ReadAllBytes(path), 960);
        if (bmp == null) return;
        _trucks.AddPhoto(t.Id, path, TruckFeatures.Describe(ImageConv.ToRgbImage(bmp)));
    }

    /// <summary>إعادة تسمية: تتحدّث الأسماء في سجلاته.</summary>
    public void Renamed(Truck t) => _swings.RenameTruck(t.Id, t.Name);

    /// <summary>حذف وايت: تبقى أسماؤه في السجلات القديمة لكن بلا ارتباط.</summary>
    public void Deleted(long id) => _swings.DetachTruck(id);

    /// <summary>يحذف صور اللقطات الأقدم من 60 يومًا ما لم تكن صور تعلّم لوايت.</summary>
    public void Cleanup()
    {
        try
        {
            var keep = _trucks.AllPhotoPaths();
            var limit = DateTime.UtcNow.AddDays(-60);
            foreach (var f in Directory.GetFiles(_snapDir))
                if (File.GetLastWriteTimeUtc(f) < limit && !keep.Contains(f)) File.Delete(f);
        }
        catch (Exception e) { EngineLog.Error("تنظيف الصور", e); }
    }
}
