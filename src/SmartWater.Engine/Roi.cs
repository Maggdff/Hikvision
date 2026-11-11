using OpenCvSharp;
using SmartWater.Core;
using System.Runtime.InteropServices;

namespace SmartWater.Engine;

/// <summary>قص نافذة ROI لكل عدّاد وتنعيم خفيف (σ=1.2) كما في النسخة الأصلية.</summary>
public static class Roi
{
    public static GrayFrame Extract(Mat full, MeterConfig m)
    {
        int half = (int)Math.Ceiling(1.1 * m.Radius) + 30;
        int x0 = Math.Max(0, (int)Math.Floor(m.Cx - half + 0.5));
        int y0 = Math.Max(0, (int)Math.Floor(m.Cy - half + 0.5));
        int x1 = Math.Min(full.Cols, (int)Math.Floor(m.Cx + half + 0.5));
        int y1 = Math.Min(full.Rows, (int)Math.Floor(m.Cy + half + 0.5));
        using var roi = new Mat(full, new Rect(x0, y0, x1 - x0, y1 - y0));
        using var blur = new Mat();
        Cv2.GaussianBlur(roi, blur, new Size(0, 0), 1.2);
        int w = blur.Cols, h = blur.Rows;
        var bytes = new byte[w * h];
        Marshal.Copy(blur.Data, bytes, 0, bytes.Length); // blur مصفوفة جديدة متصلة في الذاكرة
        return new GrayFrame(w, h, bytes, x0, y0);
    }
}
