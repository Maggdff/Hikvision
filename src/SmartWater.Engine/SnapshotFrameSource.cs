using System.Diagnostics;
using OpenCvSharp;

namespace SmartWater.Engine;

/// <summary>
/// يسحب لقطات من قناة العدّادات بمعدّل ثابت (افتراضي 4/ث) ويفكّها إلى رمادي ثم يمرّرها للمعالج.
/// الفشل يُعالج بتراجع تدريجي وإشعار OnDisconnected (فجوة وليست توقف عدّاد). يعمل 24 ساعة بحلقة لا تنتهي.
/// </summary>
public sealed class SnapshotFrameSource
{
    private readonly IsapiSnapshotClient _client;
    private readonly int _channel, _stream;
    private readonly double _fps;

    public event Action<Mat, double>? OnFrame;          // (رمادي, زمن بالثواني منذ البدء)
    public event Action<string>? OnStatus;
    public event Action? OnDisconnected;

    public long Ok { get; private set; }
    public long Failed { get; private set; }

    public SnapshotFrameSource(IsapiSnapshotClient client, int channel, int stream = 1, double fps = 4.0)
    {
        _client = client; _channel = channel; _stream = stream; _fps = fps;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        var period = TimeSpan.FromSeconds(1.0 / _fps);
        int consecutiveFail = 0;
        while (!ct.IsCancellationRequested)
        {
            var t0 = clock.Elapsed;
            byte[]? jpg = await _client.GetJpegAsync(_channel, _stream, ct).ConfigureAwait(false);
            if (jpg != null)
            {
                using var gray = Cv2.ImDecode(jpg, ImreadModes.Grayscale);
                if (!gray.Empty())
                {
                    if (consecutiveFail > 0) OnStatus?.Invoke("عاد الاتصال بالكاميرا");
                    consecutiveFail = 0; Ok++;
                    OnFrame?.Invoke(gray, clock.Elapsed.TotalSeconds);
                }
                else jpg = null;
            }
            if (jpg == null)
            {
                Failed++;
                if (++consecutiveFail == 1) { OnStatus?.Invoke($"تعذّر التقاط الصورة: {_client.LastError}"); OnDisconnected?.Invoke(); }
                var backoff = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(consecutiveFail, 5))));
                try { await Task.Delay(backoff, ct).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
                continue;
            }
            var rest = period - (clock.Elapsed - t0);
            if (rest > TimeSpan.Zero)
            {
                try { await Task.Delay(rest, ct).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
            }
        }
    }
}
