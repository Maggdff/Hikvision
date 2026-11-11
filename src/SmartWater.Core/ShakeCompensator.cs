namespace SmartWater.Core;

/// <summary>
/// تعويض اهتزاز الصورة: مطابقة NCC لحلقة الدرجات الخارجية الثابتة لوجه العدّاد (0.72R–1.02R)
/// مع إطار مرجعي، ثم استخدام الإزاحة (بدقة تحت البكسل) لتحريك مركز العدّاد.
/// </summary>
public sealed class ShakeCompensator
{
    private readonly int _search;
    private readonly double _innerR, _outerR;
    private readonly int _stride;
    private readonly double _minNcc;
    private int[] _dx = Array.Empty<int>();
    private int[] _dy = Array.Empty<int>();
    private double[] _ref = Array.Empty<double>();
    private double _refSum, _refSq;
    private int _baseX, _baseY;

    public bool HasRef { get; private set; }

    public ShakeCompensator(int search = 6, double innerR = 0.72, double outerR = 1.02, int stride = 2, double minNcc = 0.45)
    {
        _search = search; _innerR = innerR; _outerR = outerR; _stride = stride; _minNcc = minNcc;
    }

    public void SetReference(GrayFrame f, double cx, double cy, double radius)
    {
        _baseX = K.RoundInt(cx); _baseY = K.RoundInt(cy);
        var xs = new List<int>(); var ys = new List<int>(); var vs = new List<double>();
        double ro = _outerR * radius, ri = _innerR * radius;
        int lim = (int)ro + 1;
        for (int y = -lim; y <= lim; y += _stride)
        {
            for (int x = -lim; x <= lim; x += _stride)
            {
                double r = Math.Sqrt(x * x + y * y);
                if (r >= ri && r <= ro)
                {
                    int p = f.Pixel(_baseX + x, _baseY + y);
                    if (p >= 0) { xs.Add(x); ys.Add(y); vs.Add(p); }
                }
            }
        }
        _dx = xs.ToArray(); _dy = ys.ToArray(); _ref = vs.ToArray();
        _refSum = _ref.Sum(); _refSq = _ref.Sum(v => v * v);
        HasRef = _ref.Length > 100;
    }

    public sealed record Shift(double X, double Y, double Ncc);

    /// <summary>يبحث حول (lastX,lastY) بمدى ±search. null إذا كانت المطابقة ضعيفة.</summary>
    public Shift? Estimate(GrayFrame f, double lastX, double lastY)
    {
        if (!HasRef) return null;
        int n = _ref.Length;
        int cx0 = K.RoundInt(lastX), cy0 = K.RoundInt(lastY);
        int w = 2 * _search + 1;
        double denR = _refSq - _refSum * _refSum / n;
        if (denR <= 1e-6) return null;
        double best = -2.0; int bi = -1;
        for (int j = 0; j < w; j++)
        {
            for (int i = 0; i < w; i++)
            {
                int sx = cx0 + i - _search, sy = cy0 + j - _search;
                double sv = 0, svv = 0, svr = 0; bool ok = true;
                for (int k = 0; k < n; k++)
                {
                    int p = f.Pixel(_baseX + _dx[k] + sx, _baseY + _dy[k] + sy);
                    if (p < 0) { ok = false; break; }
                    double v = p;
                    sv += v; svv += v * v; svr += v * _ref[k];
                }
                if (!ok) continue;
                double denV = svv - sv * sv / n;
                if (denV <= 1e-6) continue;
                double ncc = (svr - sv * _refSum / n) / Math.Sqrt(denV * denR);
                if (ncc > best) { best = ncc; bi = j * w + i; }
            }
        }
        if (bi < 0 || best < _minNcc) return null;
        int bj = bi / w, bx = bi % w;
        double rx = cx0 + bx - _search, ry = cy0 + bj - _search;
        // تدقيق تحت البكسل: شبكة 0.25 بكسل حول أفضل نقطة صحيحة
        double fb = best, ix = rx, iy = ry;
        for (double ox = -0.5; ox <= 0.5001; ox += 0.25)
        {
            for (double oy = -0.5; oy <= 0.5001; oy += 0.25)
            {
                if (ox != 0.0 || oy != 0.0)
                {
                    double v = NccReal(f, ix + ox, iy + oy, denR);
                    if (v > fb) { fb = v; rx = ix + ox; ry = iy + oy; }
                }
            }
        }
        if (Math.Abs(rx) > 60 || Math.Abs(ry) > 60) return null;
        return new Shift(rx, ry, fb);
    }

    private double NccReal(GrayFrame f, double sx, double sy, double denR)
    {
        int n = _ref.Length;
        double sv = 0, svv = 0, svr = 0;
        for (int k = 0; k < n; k++)
        {
            double v = f.Sample(_baseX + _dx[k] + sx, _baseY + _dy[k] + sy);
            if (double.IsNaN(v)) return -2.0;
            sv += v; svv += v * v; svr += v * _ref[k];
        }
        double denV = svv - sv * sv / n;
        if (denV <= 1e-6) return -2.0;
        return (svr - sv * _refSum / n) / Math.Sqrt(denV * denR);
    }
}
