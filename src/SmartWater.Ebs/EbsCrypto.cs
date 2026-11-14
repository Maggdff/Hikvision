using System.Security.Cryptography;
using System.Text;

namespace SmartWater.Ebs;

/// <summary>
/// تشفير كلمة مرور EBS (نقل حرفي من EbsCrypto.kt المؤكَّد بالتقاط شبكة): AES/CBC/PKCS7، المفتاح والـIV نفس البايتات
/// (أول 16 بايت من النص الثابت)، Base64 بلا أسطر ثم "\n" واحدة في النهاية كما يرسلها تطبيق أندرويد الأصلي.
/// </summary>
public static class EbsCrypto
{
    private const string KeyText = "koxskfruv78dslbsxu";

    private static byte[] KeyBytes()
    {
        var k = new byte[16];
        var t = Encoding.UTF8.GetBytes(KeyText);
        Array.Copy(t, k, Math.Min(t.Length, 16));
        return k;
    }

    public static string EncryptPassword(string plain)
    {
        var key = KeyBytes();
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7; aes.Key = key; aes.IV = key;
        using var enc = aes.CreateEncryptor();
        var data = Encoding.UTF8.GetBytes(plain);
        return Convert.ToBase64String(enc.TransformFinalBlock(data, 0, data.Length)) + "\n";
    }

    public static string DecryptPassword(string base64)
    {
        var key = KeyBytes();
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7; aes.Key = key; aes.IV = key;
        using var dec = aes.CreateDecryptor();
        var data = Convert.FromBase64String(base64.Trim());
        return Encoding.UTF8.GetString(dec.TransformFinalBlock(data, 0, data.Length));
    }
}
