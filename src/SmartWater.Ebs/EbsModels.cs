using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SmartWater.Ebs;

/// <summary>إعدادات EBS. لا قيم افتراضية لاسم المستخدم/الفرع/الصندوق: تُدخَل يدويًا مطابقةً لحساب EBS الحقيقي.</summary>
public sealed record EbsSettings
{
    public string BaseUrl { get; init; } = "http://alosaimiwaterproject.ebssim.selfip.com:8080/TraderService/";
    public string UserName { get; init; } = "";
    public string Password { get; init; } = "";
    public int Year { get; init; } = DateTime.Now.Year;
    public string BranchName { get; init; } = "";
    public int BranchId { get; init; }
    public int UserId { get; init; }
    public string CashierDisplayName { get; init; } = "";
    public string Currency { get; init; } = "";
    public string DefaultPaymentType { get; init; } = "";
    public string DefaultStoreName { get; init; } = "";
    public string WaterUnitName { get; init; } = "";
    public string DefaultBarrelPrice { get; init; } = "";
    public string DeviceUuid { get; init; } = Guid.NewGuid().ToString();
}

public sealed record EbsLoginResult(bool Success, string? SessionId = null, string? DistributorName = null,
                                    string? RawResponse = null, string? ErrorMessage = null, int HttpStatusCode = 0);

/// <summary>نتيجة استدعاء عام. النجاح الحقيقي = نجاح HTTP + (لا كود أو كود EBS = 200).</summary>
public sealed record EbsOperationResult(bool HttpSuccess, int HttpStatusCode = 0, int? EbsCode = null,
                                        string? EbsMessage = null, string? RawResponse = null, JsonObject? Root = null)
{
    public bool IsFullSuccess => HttpSuccess && (EbsCode == null || EbsCode == 200);
}

internal static class EbsJson
{
    public static readonly JsonSerializerOptions Opts = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // عربي خام كما يرسله أندرويد
    };

    public static string Str(JsonNode n) => n.ToJsonString(Opts);

    public static JsonNode? TryParse(string s)
    {
        try { return JsonNode.Parse(s); } catch (JsonException) { return null; }
    }

    /// <summary>ResultBody قد يكون مصفوفة مباشرة أو نصًا يحوي مصفوفة مُرمَّزة.</summary>
    public static JsonArray? AsArray(JsonNode? n)
    {
        if (n is JsonArray a) return a;
        if (n is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s))
            return TryParse(s) as JsonArray;
        return null;
    }

    public static string? Text(JsonNode? n, string key)
    {
        if (n is not JsonObject o || !o.TryGetPropertyValue(key, out var v) || v == null) return null;
        return v is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : v.ToString();
    }

    public static double Num(JsonNode? n, string key)
    {
        var t = Text(n, key);
        return t != null && double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0.0;
    }

    /// <summary>معرّف السجل في رد السيرفر (قد يكون داخل ResultBody نصًا يحوي JSON): نفس ExtractServerId الأصلي.</summary>
    public static long ExtractServerId(JsonObject? root)
    {
        if (root == null) return 0;
        if (IdFrom(root) is long a) return a;
        foreach (var k in new[] { "ResultBody", "data", "Data" })
            if (root.TryGetPropertyValue(k, out var w) && w != null && IdFrom(w) is long b) return b;
        return 0;
    }

    private static long? IdFrom(JsonNode? e)
    {
        switch (e)
        {
            case null: return null;
            case JsonObject o:
                foreach (var k in new[] { "ID", "Id", "id" })
                {
                    if (!o.TryGetPropertyValue(k, out var v) || v == null) continue;
                    var t = v is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : v.ToString();
                    if (long.TryParse(t, out var id) && id != 0) return id;
                }
                return null;
            case JsonArray a: return a.Count > 0 ? IdFrom(a[0]) : null;
            case JsonValue v when v.TryGetValue<string>(out var str):
                str = str.Trim();
                if (str.StartsWith("[") || str.StartsWith("{")) return IdFrom(TryParse(str));
                return null;
            default: return null;
        }
    }
}

/// <summary>حفظ إعدادات EBS مشفّرة بـ DPAPI (نطاق الجهاز، فتعمل للخدمة والواجهة معًا). مكافئ Windows لـ EncryptedSharedPreferences.</summary>
public sealed class EbsSettingsStore
{
    private readonly string _path;
    public EbsSettingsStore(string? path = null) =>
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SmartWaterMonitor", "ebs-settings.bin");

    public EbsSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new EbsSettings();
            var raw = File.ReadAllBytes(_path);
            var plain = OperatingSystem.IsWindows() ? ProtectedData.Unprotect(raw, null, DataProtectionScope.LocalMachine) : raw;
            return JsonSerializer.Deserialize<EbsSettings>(Encoding.UTF8.GetString(plain)) ?? new EbsSettings();
        }
        catch { return new EbsSettings(); }
    }

    public void Save(EbsSettings s)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var plain = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(s));
        var data = OperatingSystem.IsWindows() ? ProtectedData.Protect(plain, null, DataProtectionScope.LocalMachine) : plain;
        var tmp = _path + ".tmp";
        File.WriteAllBytes(tmp, data);
        File.Move(tmp, _path, overwrite: true);
    }
}
