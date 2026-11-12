using System.Buffers.Binary;
using System.Globalization;

namespace SmartWater.Core;

/// <summary>صورة ملوّنة ARGB (بلا اعتماد على OpenCV كي تُختبر النواة وحدها).</summary>
public sealed class RgbImage
{
    public int Width { get; }
    public int Height { get; }
    public int[] Argb { get; }
    public RgbImage(int width, int height, int[] argb)
    {
        if (argb.Length < width * height) throw new ArgumentException("حجم البكسلات لا يطابق الأبعاد");
        Width = width; Height = height; Argb = argb;
    }
}

/// <summary>
/// بصمة مظهر الوايت من «الشكل واللون» فقط: توزيع الألوان في شبكة 4×3 + توزيع اتجاهات الحواف (HOG مبسّط) في شبكة 4×3.
/// الإضاءة تُطبَّع قبل الحساب حتى لا يؤثر الليل/النهار كثيرًا.
/// </summary>
public static class TruckFeatures
{
    public const int W = 64, H = 48;
    private const int GX = 4, GY = 3;
    private const int ColorBins = 16;   // 12 تدرّج لون + 4 درجات رمادي
    private const int OriBins = 8;
    public const int Dim = GX * GY * ColorBins + ColorBins + GX * GY * OriBins;
    private const float WLayout = 1.0f, WGlobal = 0.6f, WShape = 1.0f;

    public static float[] Describe(RgbImage img)
    {
        var (r, g, b) = Downscale(img);
        // تطبيع الإضاءة: متوسط السطوع → 128
        double lum = 0.0;
        for (int i = 0; i < r.Length; i++) lum += 0.299 * r[i] + 0.587 * g[i] + 0.114 * b[i];
        lum /= r.Length;
        double gain = lum < 1.0 ? 1.0 : Math.Clamp(128.0 / lum, 0.6, 1.6);
        for (int i = 0; i < r.Length; i++)
        {
            r[i] = Math.Min(r[i] * gain, 255.0);
            g[i] = Math.Min(g[i] * gain, 255.0);
            b[i] = Math.Min(b[i] * gain, 255.0);
        }

        // --- توزيع الألوان لكل خلية
        int cw = W / GX, ch = H / GY;
        var layout = new float[GX * GY * ColorBins];
        var global = new float[ColorBins];
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                int bin = ColorBin(r[i], g[i], b[i]);
                int cell = Math.Min(y / ch, GY - 1) * GX + Math.Min(x / cw, GX - 1);
                layout[cell * ColorBins + bin] += 1f;
                global[bin] += 1f;
            }
        }
        for (int c = 0; c < GX * GY; c++) Hellinger(layout, c * ColorBins, ColorBins);
        Hellinger(global, 0, ColorBins);

        // --- اتجاهات الحواف (Sobel)
        var gray = new double[W * H];
        for (int i = 0; i < gray.Length; i++) gray[i] = 0.299 * r[i] + 0.587 * g[i] + 0.114 * b[i];
        var shape = new float[GX * GY * OriBins];
        for (int y = 1; y < H - 1; y++)
        {
            for (int x = 1; x < W - 1; x++)
            {
                double P(int dx, int dy) => gray[(y + dy) * W + x + dx];
                double gx = (P(1, -1) + 2 * P(1, 0) + P(1, 1)) - (P(-1, -1) + 2 * P(-1, 0) + P(-1, 1));
                double gy = (P(-1, 1) + 2 * P(0, 1) + P(1, 1)) - (P(-1, -1) + 2 * P(0, -1) + P(1, -1));
                double mag = Math.Sqrt(gx * gx + gy * gy);
                if (mag < 8.0) continue;                        // تجاهل الضجيج الخفيف
                double ang = Math.Atan2(gy, gx); if (ang < 0) ang += Math.PI;
                int ob = Math.Clamp((int)((ang / Math.PI) * OriBins), 0, OriBins - 1);
                int cell = Math.Min(y / ch, GY - 1) * GX + Math.Min(x / cw, GX - 1);
                shape[cell * OriBins + ob] += (float)mag;
            }
        }
        L2(shape, 0, shape.Length);

        L2(layout, 0, layout.Length); L2(global, 0, global.Length);
        var o = new float[Dim];
        int k = 0;
        foreach (var v in layout) o[k++] = v * WLayout;
        foreach (var v in global) o[k++] = v * WGlobal;
        foreach (var v in shape) o[k++] = v * WShape;
        return o;
    }

    private static int ColorBin(double r, double g, double b)
    {
        double mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
        double v = mx / 255.0;
        double s = mx <= 0.0 ? 0.0 : (mx - mn) / mx;
        if (s >= 0.25 && v >= 0.2)
        {
            double d = mx - mn;
            double h;
            if (mx == r) h = ((g - b) / d) % 6.0;
            else if (mx == g) h = (b - r) / d + 2.0;
            else h = (r - g) / d + 4.0;
            h *= 60.0;
            if (h < 0) h += 360.0;
            return Math.Clamp((int)(h / 30.0), 0, 11);
        }
        return 12 + (v < 0.25 ? 0 : v < 0.5 ? 1 : v < 0.75 ? 2 : 3);
    }

    private static void Hellinger(float[] a, int off, int n)
    {
        float s = 0f; for (int i = off; i < off + n; i++) s += a[i];
        if (s <= 0f) return;
        for (int i = off; i < off + n; i++) a[i] = (float)Math.Sqrt(a[i] / s);
    }

    private static void L2(float[] a, int off, int n)
    {
        double s = 0.0; for (int i = off; i < off + n; i++) s += a[i] * a[i];
        if (s <= 1e-12) return;
        float k = (float)(1.0 / Math.Sqrt(s));
        for (int i = off; i < off + n; i++) a[i] *= k;
    }

    /// <summary>تصغير بمتوسط المساحة إلى 64×48.</summary>
    private static (double[] R, double[] G, double[] B) Downscale(RgbImage img)
    {
        var r = new double[W * H]; var g = new double[W * H]; var b = new double[W * H];
        for (int dy = 0; dy < H; dy++)
        {
            int sy0 = dy * img.Height / H;
            int sy1 = Math.Min(Math.Max(sy0 + 1, (dy + 1) * img.Height / H), img.Height);
            for (int dx = 0; dx < W; dx++)
            {
                int sx0 = dx * img.Width / W;
                int sx1 = Math.Min(Math.Max(sx0 + 1, (dx + 1) * img.Width / W), img.Width);
                long sr = 0, sg = 0, sb = 0; int n = 0;
                for (int y = sy0; y < sy1; y++)
                {
                    for (int x = sx0; x < sx1; x++)
                    {
                        int p = img.Argb[y * img.Width + x];
                        sr += (p >> 16) & 0xFF; sg += (p >> 8) & 0xFF; sb += p & 0xFF; n++;
                    }
                }
                int i = dy * W + dx;
                r[i] = (double)sr / n; g[i] = (double)sg / n; b[i] = (double)sb / n;
            }
        }
        return (r, g, b);
    }
}

public sealed record TruckSample(long TruckId, float[] Feat);

public sealed record MatchParams
{
    public double MinScore { get; init; } = 0.60;   // أقل تشابه للقبول التلقائي
    public double MinMargin { get; init; } = 0.12;  // أقل فارق عن ثاني أقرب وايت
    public int MinTrucks { get; init; } = 2;        // لا تعرّف تلقائيًا قبل وجود وايتين على الأقل لهما صور
    public int MinSamples { get; init; } = 2;       // ولا تقبل وايتًا له أقل من صورتين محفوظتين
}

/// <param name="TruckId">الوايت المقبول تلقائيًا، أو null إن لم نكن متأكدين (فتُسأل المستخدم)</param>
/// <param name="Ranking">كل الوايتات التي لها صور، من الأشبه إلى الأقل</param>
public sealed record MatchResult(long? TruckId, double Best, double Second, IReadOnlyList<(long TruckId, double Score)> Ranking, string Reason);

/// <summary>
/// مطابقة صورة جديدة مع صور الوايتات السابقة. الكاميرا ثابتة فالخلفية متشابهة في كل الصور؛ لذلك نطرح «متوسط كل الصور»
/// (متوسط متوسطات الوايتات) قبل حساب التشابه، فيبقى ما يميّز الوايتات عن بعضها لا ما يشتركون فيه.
/// </summary>
public static class TruckMatcher
{
    private static string F2(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);

    public static MatchResult Match(float[] query, IReadOnlyList<TruckSample> samples, MatchParams? p = null)
    {
        p ??= new MatchParams();
        if (samples.Count == 0) return new MatchResult(null, 0.0, 0.0, Array.Empty<(long, double)>(), "لا صور محفوظة بعد");
        var byTruck = samples.GroupBy(s => s.TruckId).ToList();       // ترتيب أول ظهور كما في Kotlin
        int dim = query.Length;

        // الخلفية المشتركة = متوسط متوسطات الوايتات (وزن متساوٍ لكل وايت)
        var mean = new float[dim];
        foreach (var grp in byTruck)
        {
            var l = grp.ToList();
            var tm = new float[dim];
            foreach (var s in l) for (int i = 0; i < dim; i++) tm[i] += s.Feat[i] / l.Count;
            for (int i = 0; i < dim; i++) mean[i] += tm[i] / byTruck.Count;
        }
        float[] Centered(float[] f) { var c = new float[dim]; for (int i = 0; i < dim; i++) c[i] = f[i] - mean[i]; return c; }
        var q = Centered(query);

        var scores = byTruck.Select(g => (Id: g.Key, Score: g.Max(s => Cosine(q, Centered(s.Feat))), Count: g.Count())).ToList();
        var ranking = scores.OrderByDescending(x => x.Score).ToList();   // OrderBy مستقر كما sortedByDescending

        var best = ranking[0];
        double second = ranking.Count > 1 ? ranking[1].Score : -1.0;
        string why;
        if (byTruck.Count < p.MinTrucks) why = "تعلّم أولي: يلزم وايتان على الأقل لهما صور";
        else if (best.Count < p.MinSamples) why = "الوايت الأقرب له صورة واحدة فقط";
        else if (best.Score < p.MinScore) why = $"التشابه منخفض ({F2(best.Score)})";
        else if (best.Score - second < p.MinMargin) why = $"وايتان متقاربان ({F2(best.Score)} مقابل {F2(second)})";
        else why = "";
        return new MatchResult(why.Length == 0 ? best.Id : null, best.Score, second,
            ranking.Select(x => (x.Id, x.Score)).ToList(), why);
    }

    public static double Cosine(float[] a, float[] b)
    {
        double d = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++) { d += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return na <= 1e-12 || nb <= 1e-12 ? 0.0 : d / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    /// <summary>تسلسل big-endian كما في نسخة أندرويد (ByteBuffer)، فتتبادل قواعد البيانات نفس الصيغة.</summary>
    public static byte[] ToBytes(float[] f)
    {
        var o = new byte[f.Length * 4];
        for (int i = 0; i < f.Length; i++) BinaryPrimitives.WriteSingleBigEndian(o.AsSpan(i * 4, 4), f[i]);
        return o;
    }

    public static float[]? FromBytes(byte[] b)
    {
        if (b.Length != TruckFeatures.Dim * 4) return null;
        var f = new float[TruckFeatures.Dim];
        for (int i = 0; i < f.Length; i++) f[i] = BinaryPrimitives.ReadSingleBigEndian(b.AsSpan(i * 4, 4));
        return f;
    }
}
