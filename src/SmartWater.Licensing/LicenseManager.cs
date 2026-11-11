using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SmartWater.Licensing;

/// <summary>مخزن مفتاح/قيمة في ملف JSON (كتابة ذرّية). المسار الافتراضي: %ProgramData%\SmartWaterMonitor\license.json.</summary>
public sealed class FileKeyValue : IKeyValue
{
    private readonly string _path;
    private readonly object _lock = new();
    private Dictionary<string, string> _d = new();

    public FileKeyValue(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SmartWaterMonitor", "license.json");
        try
        {
            if (File.Exists(_path))
                _d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path)) ?? new();
        }
        catch { _d = new(); }
    }

    public string? Get(string key) { lock (_lock) return _d.TryGetValue(key, out var v) ? v : null; }

    public void Put(string key, string? value)
    {
        lock (_lock)
        {
            if (value == null) _d.Remove(key); else _d[key] = value;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_d));
            File.Move(tmp, _path, overwrite: true);
        }
    }
}

public sealed class LicenseOptions
{
    /// <summary>قاعدة Firebase للتراخيص (نفس عنوان LICENSE_URL في نسخة أندرويد).</summary>
    public string LicenseUrl { get; init; } = "https://water-ed54e-default-rtdb.firebaseio.com";
    /// <summary>false = بناء تطوير داخلي بلا ترخيص. نسخ العملاء تُبنى دائمًا بـ true.</summary>
    public bool Required { get; init; } = true;
    public TimeSpan MinRefreshGap { get; init; } = TimeSpan.FromMinutes(20);
}

/// <summary>
/// مدير الترخيص على الكمبيوتر (واحد للخدمة): تفعيل بمفتاح، جلب الترخيص وتسجيل الجهاز، وحكم بساعة موثوقة.
/// Verdict للواجهة، وAllowed للخدمة قبل أي اتصال بالكاميرا أو عدّ. فشل الجلب يُبقي آخر حكم معروف.
/// </summary>
public sealed class LicenseManager
{
    private readonly LicenseKeeper _keeper;
    private readonly IKeyValue _kv;
    private readonly LicenseApi _api;
    private readonly LicenseOptions _opt;
    private readonly SemaphoreSlim _busy = new(1, 1);
    private long _lastTryTick;

    public string LastError { get; private set; } = "";
    public event Action<LicenseVerdict>? VerdictChanged;

    public LicenseManager(IKeyValue kv, LicenseOptions? options = null, LicenseApi? api = null,
                          Func<long>? wall = null, Func<long>? mono = null)
    {
        _kv = kv;
        _opt = options ?? new LicenseOptions();
        _api = api ?? new LicenseApi();
        _keeper = new LicenseKeeper(kv, wall ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
                                    mono ?? (() => Environment.TickCount64));
    }

    public LicenseVerdict Verdict() => _keeper.Verdict(_opt.Required);
    public bool Allowed => !Verdict().Blocked;
    public string? LicenseKeyText => _keeper.LicenseId() is { } id ? LicenseKey.Format(id) : null;

    private void Publish() => VerdictChanged?.Invoke(Verdict());

    /// <summary>معرّف الجهاز: SHA-256 لـ (MachineGuid:المفتاح) أول 16 خانة hex. على غير ويندوز/عند الفشل: رقم عشوائي محفوظ.</summary>
    private string DeviceId(string licenseId)
    {
        string? baseId = null;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                baseId = k?.GetValue("MachineGuid") as string;
            }
        }
        catch { /* نستخدم الاحتياطي */ }
        if (string.IsNullOrWhiteSpace(baseId))
        {
            baseId = _kv.Get("dev_rand");
            if (baseId == null) { baseId = Guid.NewGuid().ToString(); _kv.Put("dev_rand", baseId); }
        }
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{baseId}:{licenseId}"));
        return Convert.ToHexString(hash)[..16];
    }

    private static string DeviceName() => $"{Environment.MachineName} (Windows)";

    /// <summary>يتحقق من الترخيص ويسجّل الجهاز. يتجاهل الطلبات المتقاربة ما لم تكن force.</summary>
    public async Task RefreshAsync(bool force = false, CancellationToken ct = default)
    {
        var id = _keeper.LicenseId();
        if (id == null) return;
        long now = Environment.TickCount64;
        if (!force && _lastTryTick != 0 && now - _lastTryTick < (long)_opt.MinRefreshGap.TotalMilliseconds) return;
        if (!await _busy.WaitAsync(0, ct).ConfigureAwait(false)) return;
        try
        {
            _lastTryTick = now;
            var r = await _api.SyncAsync(_opt.LicenseUrl, id, DeviceId(id), DeviceName(), ct: ct).ConfigureAwait(false);
            switch (r)
            {
                case LicenseApi.Result.Ok ok:
                    _keeper.OnFetched(ok.Sync.Raw, ok.Sync.ServerMs, ok.Sync.DeviceOk); LastError = ""; break;
                case LicenseApi.Result.Offline:
                    LastError = "لا اتصال بالإنترنت"; _lastTryTick = 0; break;
                case LicenseApi.Result.Error e:
                    LastError = e.Msg; break;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) { LastError = e.Message; }
        finally { _busy.Release(); }
        Publish();
    }

    /// <summary>تفعيل بمفتاح (يلزم إنترنت). يعيد null عند النجاح أو رسالة الخطأ.</summary>
    public async Task<string?> ActivateAsync(string key, CancellationToken ct = default)
    {
        var id = LicenseKey.Normalize(key);
        if (id == null) return "مفتاح غير صالح: 20 حرفًا وأرقامًا (A-Z و2-7) مثل ABCD-EFGH-IJ23-4567-ABCD";
        string? msg;
        try
        {
            var r = await _api.SyncAsync(_opt.LicenseUrl, id, DeviceId(id), DeviceName(), ct: ct).ConfigureAwait(false);
            switch (r)
            {
                case LicenseApi.Result.Offline: msg = "يلزم اتصال بالإنترنت للتفعيل"; break;
                case LicenseApi.Result.Error e: msg = e.Msg; break;
                case LicenseApi.Result.Ok ok when ok.Sync.Raw == null: msg = "المفتاح غير موجود — تحقق من كتابته"; break;
                case LicenseApi.Result.Ok ok when LicenseCodec.Decode(ok.Sync.Raw) == null: msg = "مستند الترخيص تالف؛ تواصل مع مزوّد الخدمة"; break;
                case LicenseApi.Result.Ok ok:
                    _keeper.SetLicenseId(id); _keeper.OnFetched(ok.Sync.Raw, ok.Sync.ServerMs, ok.Sync.DeviceOk); LastError = ""; msg = null; break;
                default: msg = "خطأ غير متوقع"; break;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) { msg = e.Message; }
        Publish();
        return msg;
    }

    /// <summary>إلغاء التفعيل على هذا الجهاز (لتغيير المفتاح). لا يمسّ السجلات.</summary>
    public void Deactivate() { _keeper.Clear(); LastError = ""; Publish(); }

    /// <summary>ملخص للعرض في الإعدادات.</summary>
    public string Describe()
    {
        var v = Verdict();
        if (_keeper.LicenseId() == null) return _opt.Required ? "غير مفعّل" : "بناء تطوير داخلي: لا يطلب ترخيصًا";
        var d = _keeper.Doc();
        var sb = new StringBuilder();
        sb.Append(v.Blocked ? $"⛔ {v.Title}" : "✅ مفعّل" + (v.Status == LicenseStatus.Grace ? " (فترة سماح)" : ""));
        if (d != null)
        {
            sb.Append($"\nالبمب: {d.Org}");
            sb.Append("\nينتهي: ").Append(d.ValidUntilMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(d.ValidUntilMs).ToString("yyyy-MM-dd") : "بلا نهاية");
            if (v.DaysLeft is { } left) sb.Append($" (متبقٍ {left} يومًا)");
            if (d.MaxDevices > 0) sb.Append($"\nالأجهزة المسموحة: {d.MaxDevices}");
        }
        sb.Append($"\nالمفتاح: {LicenseKeyText}");
        long c = _keeper.LastContactMs();
        if (c > 0) sb.Append("\nآخر تحقق: ").Append(DateTimeOffset.FromUnixTimeMilliseconds(c).ToString("yyyy-MM-dd HH:mm"));
        if (LastError.Length > 0) sb.Append($"\n⚠ آخر محاولة: {LastError}");
        return sb.ToString();
    }
}
