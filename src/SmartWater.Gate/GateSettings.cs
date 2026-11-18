using System.Security.Cryptography;
using System.Text.Json;

namespace SmartWater.Gate;

/// <summary>إعدادات الجسر المحفوظة محليًا. سرّ البطاقات (لتجزئة UID) يُولَّد مرة واحدة ويُخزَّن مشفّرًا.</summary>
public sealed record GateSettings
{
    public bool Enabled { get; init; }
    /// <summary>عنوان الربط: يُفضَّل عنوان الشبكة المحلية للصناديق (مثل 192.168.10.10) لا 0.0.0.0.</summary>
    public string BindIp { get; init; } = "0.0.0.0";
    public int Port { get; init; } = 8080;
    public BalancePolicy Policy { get; init; } = BalancePolicy.Prepaid;
    public double CreditLimit { get; init; }
    public string CardSecretProtected { get; init; } = "";
}

public sealed class GateSettingsStore
{
    private readonly string _path;
    private readonly IKeyProtector _prot;
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    public GateSettingsStore(string path, IKeyProtector prot) { _path = path; _prot = prot; }

    public GateSettings Load()
    {
        try { return File.Exists(_path) ? JsonSerializer.Deserialize<GateSettings>(File.ReadAllText(_path), Opts) ?? new GateSettings() : new GateSettings(); }
        catch { return new GateSettings(); }
    }

    public void Save(GateSettings s)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(s, Opts));
        File.Move(tmp, _path, overwrite: true);
    }

    /// <summary>يعيد سرّ البطاقات (32 بايت)، ويولّده ويحفظه إن لم يوجد. فقدانه يُبطل كل البطاقات المسجَّلة.</summary>
    public (GateSettings Settings, byte[] Secret) EnsureSecret(GateSettings s)
    {
        if (!string.IsNullOrEmpty(s.CardSecretProtected))
        {
            try { return (s, _prot.Unprotect(Convert.FromBase64String(s.CardSecretProtected))); }
            catch { /* تالف: يُولَّد جديد (تُعاد تسمية البطاقات) */ }
        }
        var secret = RandomNumberGenerator.GetBytes(32);
        var n = s with { CardSecretProtected = Convert.ToBase64String(_prot.Protect(secret)) };
        Save(n);
        return (n, secret);
    }
}
