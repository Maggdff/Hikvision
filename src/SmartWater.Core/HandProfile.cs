namespace SmartWater.Core;

/// <summary>
/// الملف الزاوي للإضاءة حول المحور: لكل زاوية (0°=أعلى، باتجاه عقارب الساعة) متوسط السطوع على حلقة
/// نصف قطرية [inner, outer]×R ثم تنعيم بنافذة زاوية. العقرب أغمق من الوجه فيظهر كقاع عميق.
/// </summary>
public sealed class HandProfile
{
    public const int N = 360;
    private readonly int _smoothDeg;
    private readonly int _radialSteps;
    private readonly double[] _sin = new double[N];
    private readonly double[] _cos = new double[N];
    private readonly double[] _raw = new double[N];

    public HandProfile(int smoothDeg = 15, int radialSteps = 8)
    {
        _smoothDeg = smoothDeg; _radialSteps = radialSteps;
        for (int i = 0; i < N; i++)
        {
            double rad = i * Math.PI / 180.0;
            _sin[i] = Math.Sin(rad); _cos[i] = Math.Cos(rad);
        }
    }

    /// <summary>يكتب التباين النسبي ((سطوع منعَّم − متوسط)/متوسط) في out. false إذا تعذّر القياس.</summary>
    public bool Compute(GrayFrame f, double cx, double cy, double radius, double inner, double outer, double[] output)
    {
        for (int a = 0; a < N; a++)
        {
            double s = 0; int n = 0;
            for (int k = 0; k < _radialSteps; k++)
            {
                double r = radius * (inner + (outer - inner) * k / (_radialSteps - 1));
                double v = f.Sample(cx + r * _sin[a], cy - r * _cos[a]);
                if (!double.IsNaN(v)) { s += v; n++; }
            }
            if (n < _radialSteps * 0.6) return false;
            _raw[a] = s / n;
        }
        double mean = 0;
        foreach (var v in _raw) mean += v;
        mean /= N;
        if (mean < 8.0) return false; // مظلم جدًا لقياس موثوق
        int half = _smoothDeg / 2;
        int w = 2 * half + 1;
        for (int a = 0; a < N; a++)
        {
            double s = 0;
            for (int d = -half; d <= half; d++) s += _raw[(a + d + N) % N];
            output[a] = (s / w - mean) / mean;
        }
        return true;
    }
}
