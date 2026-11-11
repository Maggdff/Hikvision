using SmartWater.Core;

namespace SmartWater.Tests;

/// <summary>مولّد وجه عدّاد اصطناعي: قرص فاتح، علامات درجات في الحلقة الخارجية، محور داكن، وعقرب داكن.</summary>
public static class SyntheticMeter
{
    public const int Size = 200;
    public const double Cx = 100, Cy = 100, R = 80;

    public static GrayFrame Frame(double handDeg)
    {
        var data = new byte[Size * Size];
        double th = handDeg * Math.PI / 180.0;
        double sx = Math.Sin(th), cy = Math.Cos(th);
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                double dx = x - Cx, dy = y - Cy;
                double r = Math.Sqrt(dx * dx + dy * dy);
                double ang = (Math.Atan2(dx, -dy) * 180.0 / Math.PI + 360.0) % 360.0;
                int v = 200;
                if (r >= 0.78 * R && r <= 1.0 * R && (ang % 10.0) < 2.0) v = 60;      // علامات الدرجات
                if (r <= 0.12 * R) v = 60;                                              // المحور
                double along = dx * sx - dy * cy;                                       // الإسقاط على اتجاه العقرب
                double perp = Math.Abs(dx * cy + dy * sx);
                if (along >= 0 && along <= 0.6 * R && perp <= 3.0) v = 40;              // العقرب
                data[y * Size + x] = (byte)v;
            }
        }
        return new GrayFrame(Size, Size, data);
    }

    public static MeterSample S(double t, double angle) => new(t, true, angle, angle % 360.0, 0.5, 0, 0, false);
}
