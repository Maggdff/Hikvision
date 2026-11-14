using Microsoft.Win32;
using SmartWater.Data;
using SmartWater.Ebs;
using SmartWater.Engine;
using SmartWater.Licensing;

namespace SmartWater.App;

public static class FileLog
{
    private static readonly object L = new();
    private static string _dir = "";

    public static void Init(string dir)
    {
        _dir = dir; Directory.CreateDirectory(dir);
        try { foreach (var f in Directory.GetFiles(dir, "*.log")) if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-30)) File.Delete(f); } catch { }
    }

    public static void Write(string level, string msg)
    {
        try { lock (L) File.AppendAllText(Path.Combine(_dir, DateTime.Now.ToString("yyyy-MM-dd") + ".log"), $"{DateTime.Now:HH:mm:ss} [{level}] {msg}{Environment.NewLine}"); }
        catch { }
    }
}

public static class AutoStart
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SmartWaterMonitor";

    public static bool IsEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(KeyPath);
        return k?.GetValue(ValueName) != null;
    }

    public static void Set(bool on)
    {
        using var k = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true) ?? Registry.CurrentUser.CreateSubKey(KeyPath);
        if (on) k.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --minimized");
        else k.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

/// <summary>خدمات التطبيق المشتركة بين النافذة وأيقونة الشريط: المحرك والترخيص وEBS والمراقبة الذاتية.</summary>
public sealed class AppServices : IDisposable
{
    private const long RestartAfterSilentSec = 300;
    private EbsClient _ebsClient;
    private long _startedTick;

    public string DataDir { get; }
    public MonitorEngine Engine { get; }
    public LicenseManager License { get; }
    public EbsSettingsStore EbsStore { get; }
    public EbsDb Ebs { get; }
    public EbsSettings EbsSettings { get; private set; }
    public EbsClient EbsClient => _ebsClient;

    /// <summary>المستخدم يريد المراقبة شغّالة (تُستأنف تلقائيًا بعد الترخيص أو أي عطل).</summary>
    public bool DesiredRunning { get; set; } = true;
    public event Action<string>? Notify;

    public AppServices()
    {
        DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmartWaterMonitor");
        Directory.CreateDirectory(DataDir);
        FileLog.Init(Path.Combine(DataDir, "logs"));
        EngineLog.Info = m => FileLog.Write("INFO", m);
        EngineLog.Error = (m, e) => FileLog.Write("ERROR", e == null ? m : $"{m}: {e}");
        Engine = new MonitorEngine(DataDir);
        bool dev = Environment.GetEnvironmentVariable("SWM_DEV") == "1";     // بناء تطوير داخلي بلا ترخيص
        License = new LicenseManager(new FileKeyValue(Path.Combine(DataDir, "license.json")), new LicenseOptions { Required = !dev });
        EbsStore = new EbsSettingsStore(Path.Combine(DataDir, "ebs-settings.bin"));
        Ebs = new EbsDb(Path.Combine(DataDir, "ebs.db"));
        EbsSettings = EbsStore.Load();
        EbsStore.Save(EbsSettings);                                           // يثبّت DeviceUuid من أول تشغيل
        _ebsClient = new EbsClient(EbsSettings);
        FileLog.Write("INFO", "بدء التطبيق");
    }

    public void SaveEbsSettings(EbsSettings s)
    {
        EbsStore.Save(s);
        EbsSettings = s;
        _ebsClient = new EbsClient(s);          // جلسة جديدة بالإعدادات الجديدة
    }

    public void StartMonitoring()
    {
        DesiredRunning = true;
        var v = License.Verdict();
        if (v.Blocked) { Notify?.Invoke($"لا يمكن بدء المراقبة: {v.Title}"); return; }
        _startedTick = Environment.TickCount64;
        Engine.Start();
    }

    public void StopMonitoring() { DesiredRunning = false; Engine.Stop(); }

    /// <summary>تُستدعى كل 30 ثانية: تحقق الترخيص، تُوقف عند الحجب وتستأنف عند الاستعادة، ومراقبة ذاتية للتوقف الصامت.</summary>
    public async Task TickAsync()
    {
        try { await License.RefreshAsync().ConfigureAwait(false); } catch (Exception e) { FileLog.Write("WARN", "تحقق الترخيص: " + e.Message); }
        var v = License.Verdict();
        if (v.Blocked)
        {
            if (Engine.Running) { Engine.Stop(); Notify?.Invoke($"توقفت المراقبة: {v.Title}"); FileLog.Write("WARN", "حُجب الترخيص: " + v.Title); }
            return;
        }
        if (DesiredRunning && !Engine.Running) { FileLog.Write("INFO", "استئناف المراقبة"); StartMonitoring(); return; }
        if (Engine.Running && Environment.TickCount64 - _startedTick > RestartAfterSilentSec * 1000 && Engine.FrameAgeSec > RestartAfterSilentSec)
        {
            FileLog.Write("WARN", $"لا إطارات منذ {Engine.FrameAgeSec:0} ثانية — إعادة تشغيل المحرك");
            Notify?.Invoke("لا إطارات من الكاميرا — أُعيد تشغيل المراقبة");
            StartMonitoring();
        }
    }

    public void Dispose()
    {
        try { Engine.Dispose(); } catch { }
        try { Ebs.Dispose(); } catch { }
        FileLog.Write("INFO", "إغلاق التطبيق");
    }
}
