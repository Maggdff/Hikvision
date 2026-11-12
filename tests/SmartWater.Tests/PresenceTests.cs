using System.Runtime.InteropServices;
using OpenCvSharp;
using SmartWater.Core;
using Xunit;

namespace SmartWater.Tests;

/// <summary>
/// اختبار كاشف الوايت على لقطات حقيقية من «Camera 07» (فيديو 62ث، لقطة كل ثانيتين) — نقل حرفي لـ verify/CamTest.kt.
/// المشهد: وايت داكن واقف تحت القصبة 0–6ث ثم يخرج؛ ثم وايت بخزان أزرق يدخل ~34ث ويقف حتى نهاية الفيديو.
/// </summary>
public class PresenceTests
{
    private static readonly double[] Zone = { 0.44, 0.28, 0.46, 0.47 };   // منطقة القصبة الافتراضية
    private static List<(byte[] G, int W, int H)>? _frames;

    private static List<(byte[] G, int W, int H)> Frames()
    {
        if (_frames != null) return _frames;
        var dir = Path.Combine(AppContext.BaseDirectory, "testdata", "cam7");
        var list = new List<(byte[], int, int)>();
        foreach (var f in Directory.GetFiles(dir, "*.jpg").OrderBy(x => Path.GetFileName(x), StringComparer.Ordinal))
        {
            using var m = Cv2.ImRead(f, ImreadModes.Color);        // BGR
            int w = m.Cols, h = m.Rows;
            var bgr = new byte[w * h * 3];
            Marshal.Copy(m.Data, bgr, 0, bgr.Length);
            var g = new byte[w * h];
            for (int i = 0; i < g.Length; i++)                     // نفس معادلة النسخة الأصلية
                g[i] = (byte)((bgr[i * 3 + 2] * 299 + bgr[i * 3 + 1] * 587 + bgr[i * 3] * 114) / 1000);
            list.Add((g, w, h));
        }
        return _frames = list;
    }

    private static List<(string Kind, int Sec)> Events(int? bgIndex, double[]? zone)
    {
        var frames = Frames();
        var d = new TruckPresenceDetector(new PresenceParams { Zone = zone });
        if (bgIndex is int bi) d.SetBackground(frames[bi].G, frames[bi].W, frames[bi].H);
        var o = new List<(string, int)>();
        for (int i = 0; i < frames.Count; i++)
        {
            var e = d.Update(i * 2000L, frames[i].G, frames[i].W, frames[i].H);
            switch (e)
            {
                case PresenceEvent.Entered en: o.Add(("enter", (int)(en.TMs / 1000))); break;
                case PresenceEvent.Left le: o.Add(("left", (int)(le.TMs / 1000))); break;
                case PresenceEvent.Replaced re: o.Add(("replaced", (int)(re.TMs / 1000))); break;
            }
        }
        return o;
    }

    [Fact]
    public void Loads_the_31_real_frames()
    {
        Assert.Equal(31, Frames().Count);
        Assert.Equal(640, Frames()[0].W);
    }

    [Fact]
    public void Empty_background_with_zone_detects_enter_leave_enter_and_no_false_events()
    {
        var ev = Events(12, Zone);
        Assert.Equal(3, ev.Count);
        Assert.Equal("enter", ev[0].Kind); Assert.True(ev[0].Sec <= 2, $"enter at {ev[0].Sec}");
        Assert.Equal("left", ev[1].Kind); Assert.InRange(ev[1].Sec, 8, 26);
        Assert.Equal("enter", ev[2].Kind); Assert.InRange(ev[2].Sec, 30, 38);
    }

    [Fact]
    public void Without_zone_the_parked_truck_is_not_detected()
    {
        Assert.DoesNotContain(Events(12, null), e => e.Kind == "enter");
    }

    private static List<(int Frame, double Change)> RunRef(List<int> seq, int refFrames)
    {
        var frames = Frames();
        var d = new TruckPresenceDetector(new PresenceParams { Zone = Zone });
        d.SetBackground(frames[12].G, frames[12].W, frames[12].H);
        var o = new List<(int, double)>(); long t = 0; int n = 0;
        foreach (var i in seq)
        {
            d.Update(t, frames[i].G, frames[i].W, frames[i].H); n++;
            if (n <= refFrames) d.SetReference(); else o.Add((i, d.RefChange));
            t += 2000;
        }
        return o;
    }

    [Fact]
    public void SwapGuard_fires_when_the_first_truck_leaves_and_the_second_arrives()
    {
        var seq = Enumerable.Range(0, 4).Concat(Enumerable.Range(4, 6)).Concat(Enumerable.Range(17, 14)).ToList();
        var guard = new SwapGuard();
        int? fired = RunRef(seq, 4).Where(x => guard.Update(x.Change)).Select(x => (int?)x.Frame).FirstOrDefault();
        Assert.NotNull(fired);
        Assert.InRange(fired!.Value, 5, 22);
    }

    [Fact]
    public void SwapGuard_stays_quiet_while_the_same_truck_stands()
    {
        var seq = Enumerable.Range(0, 4).Concat(Enumerable.Repeat(3, 10)).ToList();
        var guard = new SwapGuard();
        Assert.DoesNotContain(RunRef(seq, 4), x => guard.Update(x.Change));
    }
}
