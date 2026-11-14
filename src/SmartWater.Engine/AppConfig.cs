using System.Text.Json;
using SmartWater.Core;

namespace SmartWater.Engine;

public static class EngineLog
{
    public static Action<string> Info { get; set; } = _ => { };
    public static Action<string, Exception?> Error { get; set; } = (_, _) => { };
}

/// <summary>إعدادات التشغيل (نقل لـ AppConfig في نسخة أندرويد). تُحفظ JSON في ProgramData.</summary>
public sealed record AppConfig
{
    public const string DefaultZone = "44,28,46,47";

    public string Ip { get; init; } = "192.168.1.100";
    public int HttpPort { get; init; } = 80;
    public string User { get; init; } = "admin";
    public string Pass { get; init; } = "";
    /// <summary>قناة كاميرا العدّادات (لقطات ISAPI: القناة×100 + البث).</summary>
    public int Channel { get; init; } = 4;
    public int StreamType { get; init; } = 1;
    /// <summary>قناة كاميرا الوايتات: لقطة JPEG عند بدء الحركة (0 = معطّل).</summary>
    public int TruckChannel { get; init; } = 7;
    public bool AutoRecognize { get; init; } = true;
    public int MeterCount { get; init; } = 2;
    public double SwapFrac { get; init; } = 0.35;
    public bool Diag { get; init; }
    public double BarrelsPerTurn { get; init; } = 0.5;
    /// <summary>0 = تفرّع (العدّادان ماء مختلف فيُجمعان)؛ 1 = تسلسل (الماء نفسه فلا يُجمع).</summary>
    public int Wiring { get; init; }
    public string TruckZone { get; init; } = DefaultZone;
    public int CalW { get; init; }
    public int CalH { get; init; }
    public SwingParams Swing { get; init; } = new();
    public List<MeterConfig> Meters { get; init; } = new()
    {
        new("العداد 1", 505.0, 1232.0, 60.0),
        new("العداد 2", 1887.0, 1337.0, 62.0),
    };

    public double DegPerBarrel => 360.0 / Math.Clamp(BarrelsPerTurn, 0.01, 10.0);
    public IReadOnlyList<MeterConfig> ActiveMeters => Meters.Take(Math.Clamp(MeterCount, 1, Math.Max(1, Meters.Count))).ToList();
}

public sealed class ConfigStore
{
    private readonly string _dir;
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public ConfigStore(string? dir = null) =>
        _dir = dir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SmartWaterMonitor");

    private string ConfigPath => Path.Combine(_dir, "config.json");

    public AppConfig Load()
    {
        try { return File.Exists(ConfigPath) ? JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath)) ?? new() : new(); }
        catch { return new(); }
    }

    public void Save(AppConfig c)
    {
        Directory.CreateDirectory(_dir);
        var tmp = ConfigPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(c, Opts));
        File.Move(tmp, ConfigPath, overwrite: true);
    }

    /// <summary>مرجع المعايرة: نافذة ROI رمادية تُستخدم لتعويض الاهتزاز.</summary>
    public void SaveReference(int i, GrayFrame f)
    {
        Directory.CreateDirectory(_dir);
        using var w = new BinaryWriter(File.Create(Path.Combine(_dir, $"ref_{i}.bin")));
        w.Write(f.Width); w.Write(f.Height); w.Write(f.OriginX); w.Write(f.OriginY);
        w.Write(f.Data, 0, f.Width * f.Height);
    }

    public GrayFrame? LoadReference(int i)
    {
        try
        {
            var p = Path.Combine(_dir, $"ref_{i}.bin");
            if (!File.Exists(p)) return null;
            using var r = new BinaryReader(File.OpenRead(p));
            int w = r.ReadInt32(), h = r.ReadInt32(), ox = r.ReadInt32(), oy = r.ReadInt32();
            if (w <= 0 || h <= 0 || w > 8192 || h > 8192) return null;
            return new GrayFrame(w, h, r.ReadBytes(w * h), ox, oy);
        }
        catch { return null; }
    }
}
