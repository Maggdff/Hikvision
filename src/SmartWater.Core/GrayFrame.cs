namespace SmartWater.Core;

/// <summary>
/// إطار رمادي (8 بت): كامل أو نافذة ROI. الإحداثيات المطلقة (في إطار الكاميرا الكامل)
/// تُحوَّل داخليًا بطرح (OriginX, OriginY). النواة بلا أي اعتماد على OpenCV أو الواجهة.
/// </summary>
public sealed class GrayFrame
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Data { get; }
    public int OriginX { get; }
    public int OriginY { get; }

    public GrayFrame(int width, int height, byte[] data, int originX = 0, int originY = 0)
    {
        if (data.Length < width * height) throw new ArgumentException("حجم البيانات لا يطابق الأبعاد");
        Width = width; Height = height; Data = data; OriginX = originX; OriginY = originY;
    }

    /// <summary>قيمة بكسل صحيح بإحداثيات مطلقة، أو -1 خارج الإطار.</summary>
    public int Pixel(int x, int y)
    {
        int lx = x - OriginX, ly = y - OriginY;
        if (lx < 0 || ly < 0 || lx >= Width || ly >= Height) return -1;
        return Data[ly * Width + lx];
    }

    /// <summary>عيّنة ثنائية الخطية بإحداثيات مطلقة، NaN خارج الإطار.</summary>
    public double Sample(double x, double y)
    {
        double lx = x - OriginX, ly = y - OriginY;
        int x0 = (int)Math.Floor(lx), y0 = (int)Math.Floor(ly);
        if (x0 < 0 || y0 < 0 || x0 + 1 >= Width || y0 + 1 >= Height) return double.NaN;
        double fx = lx - x0, fy = ly - y0;
        int i = y0 * Width + x0;
        int a = Data[i], b = Data[i + 1], c = Data[i + Width], d = Data[i + Width + 1];
        return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy;
    }
}

/// <summary>التقريب كما في Kotlin (نصف إلى الأعلى) وليس تقريب المصرفي الافتراضي في .NET.</summary>
internal static class K
{
    public static int RoundInt(double x) => (int)Math.Floor(x + 0.5);
    public static int Mod(int a, int n) => ((a % n) + n) % n;
}
