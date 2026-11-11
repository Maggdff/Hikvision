using OpenCvSharp;
using SmartWater.Core;

namespace SmartWater.Engine;

/// <summary>تتبّع العدّادات لإطارات البث؛ العيّنات تُسلَّم إلى sink (جلسة التعبئة).</summary>
public sealed class Pipeline
{
    private readonly IReadOnlyList<MeterConfig> _meters;
    private readonly Action<double, IReadOnlyList<MeterSample>> _sink;
    public IReadOnlyList<MeterTracker> Trackers { get; }
    public IReadOnlyList<MeterSample> Samples { get; private set; } = Array.Empty<MeterSample>();
    public int Frames { get; private set; }
    public int[] Invalid { get; }

    public Pipeline(IReadOnlyList<MeterConfig> meters, IReadOnlyList<GrayFrame?> refs,
                    Action<double, IReadOnlyList<MeterSample>> sink)
    {
        _meters = meters; _sink = sink;
        Trackers = meters.Select((m, i) => new MeterTracker(m, new TrackerParams(), i < refs.Count ? refs[i] : null)).ToList();
        Invalid = new int[meters.Count];
    }

    public void Process(Mat full, double t)
    {
        var s = new List<MeterSample>(_meters.Count);
        for (int i = 0; i < _meters.Count; i++)
        {
            var roi = Roi.Extract(full, _meters[i]);
            s.Add(Trackers[i].Update(roi, t));
            if (!s[i].Valid) Invalid[i]++;
        }
        Samples = s;
        Frames++;
        _sink(t, s);
    }
}
