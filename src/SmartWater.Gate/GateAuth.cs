using System.Security.Cryptography;
using System.Text;

namespace SmartWater.Gate;

public enum AuthStatus { Ok, UnknownDevice, BadSignature, StaleTime, Replay }

/// <summary>
/// توقيع الطلبات بين ESP32 والجسر: HMAC-SHA256 على
/// «METHOD\nPATH\nDEVICE\nSEQ\nTS\nNONCE\nSHA256(BODY) بالـhex الصغير»، والنتيجة hex صغير.
/// الترويسات: X-Device, X-Seq, X-Ts, X-Nonce, X-Sig. التسلسل يتزايد دومًا، والوقت ضمن ±30 ث، وnonce لا يتكرر 5 دقائق.
/// </summary>
public static class GateAuth
{
    public static string Canonical(string method, string path, string deviceId, long seq, long ts, string nonce, byte[] body) =>
        $"{method.ToUpperInvariant()}\n{path}\n{deviceId}\n{seq}\n{ts}\n{nonce}\n{Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant()}";

    public static string Sign(byte[] key, string method, string path, string deviceId, long seq, long ts, string nonce, byte[] body)
    {
        using var h = new HMACSHA256(key);
        return Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes(Canonical(method, path, deviceId, seq, ts, nonce, body)))).ToLowerInvariant();
    }

    public static bool SigEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a.ToLowerInvariant()), Encoding.ASCII.GetBytes(b.ToLowerInvariant()));
}

/// <summary>حماية مفتاح الجهاز المخزَّن (DPAPI على ويندوز).</summary>
public interface IKeyProtector { byte[] Protect(byte[] plain); byte[] Unprotect(byte[] data); }

public sealed class PlainKeyProtector : IKeyProtector
{
    public byte[] Protect(byte[] plain) => plain;
    public byte[] Unprotect(byte[] data) => data;
}

public sealed class DpapiKeyProtector : IKeyProtector
{
    public byte[] Protect(byte[] plain) => OperatingSystem.IsWindows()
        ? ProtectedData.Protect(plain, null, DataProtectionScope.LocalMachine) : plain;
    public byte[] Unprotect(byte[] data) => OperatingSystem.IsWindows()
        ? ProtectedData.Unprotect(data, null, DataProtectionScope.LocalMachine) : data;
}
