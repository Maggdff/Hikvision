using System.Runtime.InteropServices;
using OpenCvSharp;
using SmartWater.Core;

namespace SmartWater.Engine;

public static class ImageConv
{
    /// <summary>Mat بثلاث قنوات BGR (8 بت) إلى RgbImage (ARGB) لبصمة الوايت.</summary>
    public static RgbImage ToRgbImage(Mat bgr)
    {
        if (bgr.Type() != MatType.CV_8UC3) throw new ArgumentException("المطلوب صورة BGR بثلاث قنوات");
        using var cont = bgr.IsContinuous() ? bgr.Clone() : bgr.Clone();   // نسخة متصلة في الذاكرة
        int w = cont.Cols, h = cont.Rows;
        var raw = new byte[w * h * 3];
        Marshal.Copy(cont.Data, raw, 0, raw.Length);
        var argb = new int[w * h];
        for (int i = 0; i < argb.Length; i++)
            argb[i] = unchecked((int)0xFF000000u) | (raw[i * 3 + 2] << 16) | (raw[i * 3 + 1] << 8) | raw[i * 3];
        return new RgbImage(w, h, argb);
    }

    /// <summary>Mat ملوّن إلى رمادي بنفس معادلة النسخة الأصلية (299/587/114)، للكاشف.</summary>
    public static (byte[] Gray, int W, int H) ToGrayBytes(Mat bgr)
    {
        using var cont = bgr.Clone();
        int w = cont.Cols, h = cont.Rows;
        var raw = new byte[w * h * 3];
        Marshal.Copy(cont.Data, raw, 0, raw.Length);
        var g = new byte[w * h];
        for (int i = 0; i < g.Length; i++) g[i] = (byte)((raw[i * 3 + 2] * 299 + raw[i * 3 + 1] * 587 + raw[i * 3] * 114) / 1000);
        return (g, w, h);
    }
}
