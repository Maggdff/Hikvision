using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using OpenCvSharp;
using SmartWater.Core;
using SmartWater.Engine;
using Xunit;

namespace SmartWater.Tests;

/// <summary>كاميرا وهمية: تعيد لقطة JPEG لوجه العدّاد وعقربه يدور 90°/ث بالزمن الحقيقي.</summary>
internal sealed class FakeCamera : HttpMessageHandler
{
    private readonly Stopwatch _clock = new();
    public int Requests;
    public double SpeedDegPerSec = 90.0;
    public volatile bool Down;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        if (Down) throw new HttpRequestException("camera offline");
        if (!_clock.IsRunning) _clock.Start();
        Interlocked.Increment(ref Requests);
        var path = req.RequestUri!.AbsolutePath;
        if (!path.Contains("/ISAPI/Streaming/channels/401/picture")) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        var g = SyntheticMeter.Frame(30 + SpeedDegPerSec * _clock.Elapsed.TotalSeconds);
        using var m = new Mat(g.Height, g.Width, MatType.CV_8UC1);
        Marshal.Copy(g.Data, 0, m.Data, g.Data.Length);
        Cv2.ImEncode(".jpg", m, out var jpg, new ImageEncodingParam(ImwriteFlags.JpegQuality, 95));
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(jpg) });
    }
}

public sealed class MonitorEngineTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "swm-eng-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static AppConfig Cfg() => new()
    {
        Ip = "cam", HttpPort = 80, Channel = 4, StreamType = 1, MeterCount = 1, TruckChannel = 0,
        Meters = new() { new MeterConfig("عداد", SyntheticMeter.Cx, SyntheticMeter.Cy, SyntheticMeter.R) },
    };

    [Fact]
    public async Task Live_frames_through_isapi_move_the_tracker_and_create_one_swing_record()
    {
        var cam = new FakeCamera();
        new ConfigStore(_dir).Save(Cfg());
        using var eng = new MonitorEngine(_dir, c => new IsapiSnapshotClient(c.Ip, c.HttpPort, c.User, c.Pass, handler: cam));
        var states = new List<UiState>(); var msgs = new List<string>();
        eng.StateChanged += s => { lock (states) states.Add(s); };
        eng.Status += m => { lock (msgs) msgs.Add(m); };
        eng.Start();
        await Task.Delay(6000);
        Assert.True(eng.Running); Assert.True(eng.Connected);
        double total = eng.Trackers[0].TotalDegrees;
        Assert.InRange(total, 250, 620);                                          // ≈ 90°/ث × ~5.5ث
        Assert.True(eng.FrameAgeSec < 3.0);
        lock (states) Assert.Contains(states, s => s.Meters[0].Valid);
        eng.Stop();                                                               // يغلق الجلسة الجارية ويعتمد ما قاسته
        Assert.False(eng.Running);
        var rec = Assert.Single(eng.Swings.Query(null));                          // سجل واحد للجلسة
        Assert.True(rec.Measured > 0.3, $"measured {rec.Measured}");
    }

    [Fact]
    public async Task Camera_outage_is_reported_as_a_gap_and_counting_resumes_after_reconnect()
    {
        var cam = new FakeCamera();
        new ConfigStore(_dir).Save(Cfg());
        using var eng = new MonitorEngine(_dir, c => new IsapiSnapshotClient(c.Ip, c.HttpPort, c.User, c.Pass, handler: cam));
        var msgs = new List<string>();
        eng.Status += m => { lock (msgs) msgs.Add(m); };
        eng.Start();
        await Task.Delay(2500);
        double before = eng.Trackers[0].TotalDegrees;
        Assert.True(before > 50);
        cam.Down = true;
        await Task.Delay(2500);
        lock (msgs) Assert.Contains(msgs, m => m.Contains("انقطع البث"));
        Assert.False(eng.Connected);
        cam.Down = false;
        await Task.Delay(7000);                                                   // التراجع الأسّي بين المحاولات ثم العودة
        Assert.True(eng.Connected);
        Assert.True(eng.Trackers[0].TotalDegrees > before);                        // يواصل العدّ بعد العودة
        eng.Stop();
    }

    [Fact]
    public void Calibration_is_refused_until_a_still_is_captured()
    {
        using var eng = new MonitorEngine(_dir);
        Assert.Contains("التقط لقطة", eng.SaveCalibration(Cfg()));
    }
}
