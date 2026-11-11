using SmartWater.Core;
using Xunit;
using static SmartWater.Tests.SyntheticMeter;

namespace SmartWater.Tests;

public class MeterTrackerTests
{
    [Fact]
    public void Tracks_constant_rotation_without_phantom_turns()
    {
        var tracker = new MeterTracker(new MeterConfig("m", Cx, Cy, R), reference: Frame(30));
        const double speed = 60.0, fps = 5.0;
        const int frames = 50;
        MeterSample last = null!;
        for (int i = 0; i < frames; i++)
        {
            double t = i / fps;
            last = tracker.Update(Frame(30 + speed * t), t);
        }
        double expected = speed * (frames - 1) / fps;   // 588°
        Assert.True(last.Valid);
        Assert.InRange(tracker.TotalDegrees, expected - 25, expected + 25);
    }

    [Fact]
    public void Static_hand_does_not_accumulate()
    {
        var tracker = new MeterTracker(new MeterConfig("m", Cx, Cy, R), reference: Frame(90));
        for (int i = 0; i < 40; i++) tracker.Update(Frame(90), i * 0.2);
        Assert.InRange(tracker.TotalDegrees, 0, 3);
    }
}

public class SwingEngineTests
{
    [Fact]
    public void One_full_barrel_ramp_gives_one_result_of_one_barrel()
    {
        var results = new List<SwingResult>();
        var eng = new SwingEngine(1, new SwingParams(), results.Add);
        for (int i = 0; i <= 60; i++)
        {
            double t = i * 0.2;
            eng.Update(t, new MeterSample?[] { S(t, Math.Min(720, 60 * t)) });
        }
        for (double t = 12.2; t <= 20; t += 0.2) eng.Update(t, new MeterSample?[] { S(t, 720) });
        Assert.Single(results);
        Assert.Equal(SwingType.Meter, results[0].Type);
        Assert.Equal(1.0, results[0].TotalBarrels, 2);
    }

    [Fact]
    public void Tiny_wobble_is_ignored()
    {
        var results = new List<SwingResult>();
        var eng = new SwingEngine(1, new SwingParams(), results.Add);
        for (int i = 0; i < 100; i++)
        {
            double t = i * 0.2;
            eng.Update(t, new MeterSample?[] { S(t, i < 10 ? i * 1.0 : 9.0) });   // 9° فقط
        }
        Assert.Empty(results);
    }
}

public class FillSessionTests
{
    private static FillSession NewSession(Wiring w, List<FillSnapshot> finals) =>
        new("t1", 0, 1, w, new SwingParams(), 60.0, (s, _) => finals.Add(s));

    private static void RampThenIdle(FillSession fs, int idleUntilSec)
    {
        for (int i = 0; i <= 60; i++)
        {
            double t = i * 0.2;
            fs.Feed((long)(t * 1000), new MeterSample?[] { S(t, Math.Min(720, 60 * t)) });
        }
        for (int sec = 13; sec <= idleUntilSec; sec++)
            fs.Feed(sec * 1000L, new MeterSample?[] { S(sec, 720) });
    }

    [Fact]
    public void Waits_for_grace_then_finalizes_and_pauses_while_truck_present()
    {
        var finals = new List<FillSnapshot>();
        var fs = NewSession(Wiring.Parallel, finals);
        RampThenIdle(fs, 40);
        Assert.Empty(finals);                                   // ما زالت المهلة جارية
        Assert.Equal(FillState.StopWait, fs.Snapshot().State);
        for (int sec = 41; sec <= 90; sec++) fs.Feed(sec * 1000L, new MeterSample?[] { S(sec, 720) });
        Assert.Single(finals);
        Assert.Equal(1.0, finals[0].TotalBarrels, 2);
        Assert.Equal(FillState.Paused, fs.Snapshot().State);    // الوايت ما زال موجودًا
        fs.TruckLeft(91_000);
        Assert.Equal(FillState.Completed, fs.Snapshot().State);
    }

    [Fact]
    public void Flow_resuming_inside_grace_keeps_same_session_without_reset()
    {
        var finals = new List<FillSnapshot>();
        var fs = NewSession(Wiring.Parallel, finals);
        RampThenIdle(fs, 30);
        // يعود التدفق: 720° إضافية
        for (int i = 1; i <= 60; i++)
        {
            double t = 30 + i * 0.2;
            fs.Feed((long)(t * 1000), new MeterSample?[] { S(t, 720 + Math.Min(720, 60 * (t - 30))) });
        }
        for (int sec = 43; sec <= 120; sec++) fs.Feed(sec * 1000L, new MeterSample?[] { S(sec, 1440) });
        Assert.Single(finals);
        Assert.Equal(2.0, finals[0].TotalBarrels, 1);
    }

    [Fact]
    public void Series_wiring_takes_max_not_sum()
    {
        Assert.Equal(1.5, Wiring.Series.Combine(new[] { 1.0, 1.5 }));
        Assert.Equal(2.5, Wiring.Parallel.Combine(new[] { 1.0, 1.5 }));
    }
}
