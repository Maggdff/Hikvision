using OpenCvSharp;
using SmartWater.Core;
using SmartWater.Data;
using SmartWater.Engine;
using Xunit;
using static SmartWater.Tests.SyntheticMeter;

namespace SmartWater.Tests;

internal sealed class FakeSnapshots : ISnapshotProvider
{
    public static byte[] SyntheticJpeg()
    {
        using var m = new Mat(240, 320, MatType.CV_8UC3, new Scalar(40, 90, 200));
        Cv2.Rectangle(m, new Rect(60, 60, 200, 110), new Scalar(220, 220, 220), -1);
        Cv2.ImEncode(".jpg", m, out var buf);
        return buf;
    }
    private readonly byte[] _jpg = SyntheticJpeg();
    public Task<byte[]?> SnapshotJpegAsync(int channel, CancellationToken ct = default) => Task.FromResult<byte[]?>(_jpg);
}

/// <summary>سيناريوهات كاملة لتدفق التعبئة الحي: عدّاد + قاعدة بيانات + منسّق الوايت (+ كشف وجود بلقطات مصنوعة).</summary>
public sealed class LiveFillTests : IDisposable
{
    private const long T0 = 1_800_000_000_000L;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "swm-test-" + Guid.NewGuid().ToString("N"));
    private readonly SwingDb _swings = new(":memory:");
    private readonly TruckDb _trucks = new(":memory:");
    private readonly AppConfig _cfg = new() { MeterCount = 1, TruckChannel = 7 };
    private readonly TruckCoordinator _coord;
    private readonly LiveFill _live;
    private long _now = T0;
    private readonly List<TruckPrompt> _prompts = new();
    private FillStatus? _last;

    public LiveFillTests()
    {
        _coord = new TruckCoordinator(_trucks, _swings, () => _cfg, () => new FakeSnapshots(), _dir);
        _coord.Prompt += p => { lock (_prompts) _prompts.Add(p); };
        _live = new LiveFill(() => _cfg, () => new FakeSnapshots(), _coord, _ => { }, s => _last = s, () => _now);
    }

    public void Dispose()
    {
        _swings.Dispose(); _trucks.Dispose();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private void Step(double t, double angle)
    {
        _now = T0 + (long)(t * 1000);
        _live.Feed(t, new[] { S(t, angle) });
    }

    private const int W = 640, H = 480;
    private static byte[] Bg() => Enumerable.Repeat((byte)100, W * H).ToArray();
    private static byte[] TruckFrame()
    {
        var g = Bg();
        for (int y = (int)(H * 0.25); y < (int)(H * 0.8); y++)
            for (int x = (int)(W * 0.40); x < (int)(W * 0.95); x++) g[y * W + x] = 220;
        return g;
    }

    private void Presence(double t, byte[] frame)
    {
        _now = T0 + (long)(t * 1000);
        _live.ProcessPresence(_now, frame, W, H, null);
    }

    [Fact]
    public async Task One_fill_makes_one_record_and_the_chosen_truck_keeps_its_entered_quantity()
    {
        var truck = _trucks.Add("وايت 1", 55.0);
        for (int i = 0; i <= 60; i++) { double t = i * 0.2; Step(t, Math.Min(720, 60 * t)); }      // برميل كامل (720°)
        await _coord.WaitForCapturesAsync();
        TruckPrompt prompt;
        lock (_prompts) prompt = Assert.Single(_prompts);                                        // لم يتعرّف تلقائيًا: يسأل
        Assert.Contains(truck.Id, prompt.Trucks.Select(x => x.Id));
        Assert.True(_coord.Choose(prompt.EpisodeId, truck.Id));
        Assert.False(_coord.Choose(prompt.EpisodeId, truck.Id));                                 // الجواب الأول هو المعتمد
        for (int sec = 13; sec <= 90; sec++) Step(sec, 720);                                     // سكون ← مهلة الدقيقة ← إغلاق
        var all = _swings.Query(null);
        var rec = Assert.Single(all);                                                            // سجل واحد للجلسة
        Assert.Equal("وايت 1", rec.TruckName);
        Assert.Equal(55.0, rec.Total);                                                           // الكمية المدخلة لا المقاسة
        Assert.InRange(rec.Measured, 0.95, 1.05);                                                // المقاس ≈ 1 برميل
        Assert.True(rec.Full);
        Assert.Equal(FillState.Completed, _live.Status().State);
    }

    [Fact]
    public void Truck_leaving_closes_the_paused_session_and_records_once()
    {
        Presence(0, Bg());                                                                       // أول لقطة = خلفية فارغة
        Presence(2, TruckFrame()); Presence(4, TruckFrame()); Presence(6, TruckFrame());         // دخل وايت
        for (int i = 0; i <= 60; i++)                                                            // تعبئة 12 ثانية
        {
            double t = 8 + i * 0.2;
            Step(t, Math.Min(720, 60 * (t - 8)));
            if (i % 10 == 0) Presence(t, TruckFrame());
        }
        for (int sec = 21; sec <= 104; sec++)                                                    // سكون والوايت واقف
        {
            Step(sec, 720);
            if (sec % 2 == 0) Presence(sec, TruckFrame());
        }
        Assert.Equal(FillState.Paused, _live.Status().State);                                    // لم تُغلق: الوايت ما زال تحت القصبة
        Assert.Single(_swings.Query(null));                                                      // السجل معتمد بعد المهلة
        for (int sec = 105; sec <= 112; sec++)                                                   // غادر الوايت
        {
            Step(sec, 720);
            if (sec % 2 == 0) Presence(sec, Bg());
        }
        var rec = Assert.Single(_swings.Query(null));
        Assert.InRange(rec.Measured, 0.95, 1.05);
        Assert.Equal(FillState.Completed, _live.Status().State);
        Assert.Equal("اكتملت التعبئة", _live.Status().Label);
    }

    [Fact]
    public void Unknown_water_stop_without_flow_never_creates_a_record()
    {
        for (int i = 0; i < 200; i++) Step(i * 0.5, 0);                                          // عقرب ثابت
        Assert.Empty(_swings.Query(null));
        Assert.Equal(FillState.WaitingFlow, _live.Status().State);
    }
}
