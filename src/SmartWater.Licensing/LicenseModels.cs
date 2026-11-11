using System.Text;
using System.Text.RegularExpressions;

namespace SmartWater.Licensing;

/// <summary>
/// ترخيص بمب (عميل) واحد: مستند نصّي يكتبه المطوّر فقط (من swm-license-console.html) في swm/lic/{مفتاح}.
/// الصيغة والحقول مطابقة حرفيًا لنسخة أندرويد، فتعمل لوحة المطوّر الحالية مع نسخة ويندوز دون أي تغيير.
/// </summary>
public sealed record LicenseDoc(
    long Rev, string Org, bool Active, long StartMs, long ValidUntilMs, int GraceDays,
    bool Maintenance, string MaintMsg, string Notice, string Reason,
    int OfflineDays, int MaxDevices, long UpdatedMs);

public static class LicenseCodec
{
    public const string Prefix = "SWL1";
    private const int Fields = 14;
    private static readonly Regex Digits = new(@"^[0-9]{1,15}\z", RegexOptions.Compiled);

    private static string Esc(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '%': sb.Append("%25"); break;
                case '|': sb.Append("%7C"); break;
                case '\n': sb.Append("%0A"); break;
                case '\r': sb.Append("%0D"); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }

    private static string? Unesc(string s)
    {
        var sb = new StringBuilder();
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (c != '%') { sb.Append(c); i++; continue; }
            if (i + 3 > s.Length) return null;
            switch (s.Substring(i, 3))
            {
                case "%25": sb.Append('%'); break;
                case "%7C": case "%7c": sb.Append('|'); break;
                case "%0A": case "%0a": sb.Append('\n'); break;
                case "%0D": case "%0d": sb.Append('\r'); break;
                default: return null;
            }
            i += 3;
        }
        return sb.ToString();
    }

    public static string Encode(LicenseDoc d) => string.Join("|", new object[]
    {
        Prefix, d.Rev, Esc(d.Org), d.Active ? "A" : "S", d.StartMs, d.ValidUntilMs, d.GraceDays,
        d.Maintenance ? 1 : 0, Esc(d.MaintMsg), Esc(d.Notice), Esc(d.Reason), d.OfflineDays, d.MaxDevices, d.UpdatedMs,
    });

    /// <summary>null = نص غير صالح (لا يُعامَل كترخيص).</summary>
    public static LicenseDoc? Decode(string? text)
    {
        if (text == null) return null;
        var p = text.Trim().Split('|');
        if (p.Length != Fields || p[0] != Prefix) return null;
        static long? Num(string x) => Digits.IsMatch(x) ? long.Parse(x) : null;   // أرقام موجبة صريحة فقط
        bool active;
        switch (p[3]) { case "A": active = true; break; case "S": active = false; break; default: return null; }
        bool maint;
        switch (p[7]) { case "1": maint = true; break; case "0": maint = false; break; default: return null; }
        var rev = Num(p[1]); var start = Num(p[4]); var until = Num(p[5]);
        var grace = Num(p[6]); var offline = Num(p[11]); var maxDev = Num(p[12]); var updated = Num(p[13]);
        if (rev == null || start == null || until == null || grace == null || offline == null || maxDev == null || updated == null) return null;
        if (grace > int.MaxValue || offline > int.MaxValue || maxDev > int.MaxValue) return null;
        var org = Unesc(p[2]); var mm = Unesc(p[8]); var notice = Unesc(p[9]); var reason = Unesc(p[10]);
        if (org == null || mm == null || notice == null || reason == null) return null;
        return new LicenseDoc(rev.Value, org, active, start.Value, until.Value, (int)grace.Value, maint, mm, notice, reason,
            (int)offline.Value, (int)maxDev.Value, updated.Value);
    }
}

public enum LicenseStatus
{
    NotActivated,   // لم يُدخَل مفتاح تفعيل بعد
    Unmanaged,      // بناء لا يطلب ترخيصًا (تطوير داخلي)
    Active, Grace, Expired, Suspended, Maintenance, OfflineLimit,
    DeviceLimit,    // تجاوز عدد الأجهزة المسموحة
    Revoked,        // حُذف الترخيص من الخادم
}

/// <summary>Blocked = العدّ والاتصال متوقفان (يبقى السجل والتصدير). Banner = تنبيه لا يوقف الخدمة.</summary>
public sealed record LicenseVerdict(
    LicenseStatus Status, bool Blocked, string Title, string Message,
    string Banner = "", long? DaysLeft = null, string Org = "")
{
    public static readonly LicenseVerdict Unmanaged = new(LicenseStatus.Unmanaged, false, "", "");
    public static readonly LicenseVerdict NotActivated = new(LicenseStatus.NotActivated, true, "البرنامج غير مفعّل",
        "أدخل مفتاح التفعيل الذي زوّدك به مزوّد الخدمة (يلزم اتصال بالإنترنت لأول تفعيل).");
}

public static class LicenseGate
{
    public const long DayMs = 86_400_000L;
    public const long WarnDays = 14;
    private const string Safe = "سجلات الزفات محفوظة كما هي ولم يُحذف منها شيء، ويمكنك الاطلاع عليها وتصديرها الآن. يعود العدّ فور استعادة الترخيص.";

    private static long DaysCeil(long ms) => ms <= 0 ? 0 : (ms + DayMs - 1) / DayMs;
    private static string JoinNotice(string a, string b) => string.Join("\n", new[] { a, b }.Where(x => !string.IsNullOrWhiteSpace(x)));

    /// <param name="doc">null = لا مستند (غير مفعّل)</param>
    /// <param name="revoked">كان مفعّلًا ثم اختفى مستنده من الخادم</param>
    public static LicenseVerdict Evaluate(LicenseDoc? doc, long nowMs, long? lastContactMs = null, bool deviceOk = true, bool revoked = false)
    {
        long lastContact = lastContactMs ?? nowMs;
        if (revoked)
            return new(LicenseStatus.Revoked, true, "أُلغي الترخيص",
                $"أُلغي ترخيص هذا البرنامج من مزوّد الخدمة. تواصل معه لإعادة التفعيل.\n\n{Safe}");
        if (doc == null) return LicenseVerdict.NotActivated;
        string org = doc.Org;

        if (!doc.Active)
        {
            var r = doc.Reason.Trim();
            string why = r.Length == 0 ? "" : $"\nالسبب: {r}";
            return new(LicenseStatus.Suspended, true, "الترخيص معلّق", $"أُوقفت الخدمة مؤقتًا من مزوّد الخدمة.{why}\n\n{Safe}", Org: org);
        }

        long until = doc.ValidUntilMs;
        long graceEnd = until > 0 ? until + doc.GraceDays * DayMs : 0L;
        if (until > 0 && nowMs > graceEnd)
            return new(LicenseStatus.Expired, true, "انتهى الترخيص",
                $"انتهت مدة الترخيص وفترة السماح. تواصل مع مزوّد الخدمة لتجديده.\n\n{Safe}", Org: org);

        if (!deviceOk)
            return new(LicenseStatus.DeviceLimit, true, "تجاوز عدد الأجهزة",
                $"هذا الترخيص مسموح لـ {doc.MaxDevices} جهازًا فقط وقد استُنفدت. تواصل مع مزوّد الخدمة لإضافة جهاز أو إزالة جهاز قديم.\n\n{Safe}", Org: org);

        if (doc.Maintenance)
        {
            var m = doc.MaintMsg.Trim();
            if (m.Length == 0) m = "النظام تحت الصيانة حاليًا. سيعود للعمل قريبًا.";
            return new(LicenseStatus.Maintenance, true, "النظام تحت الصيانة", $"{m}\n\n{Safe}", Org: org);
        }

        if (doc.OfflineDays > 0 && nowMs - lastContact > doc.OfflineDays * DayMs)
            return new(LicenseStatus.OfflineLimit, true, "يلزم التحقق من الترخيص",
                $"لم يتصل هذا الكمبيوتر بالإنترنت للتحقق من الترخيص منذ أكثر من {doc.OfflineDays} يومًا. اتصل بالإنترنت ثم أعد المحاولة.\n\n{Safe}", Org: org);

        string notice = doc.Notice.Trim();
        if (until > 0 && nowMs > until)
        {
            long leftG = DaysCeil(graceEnd - nowMs);
            string b = $"انتهى الترخيص وأنت في فترة السماح: يتوقف العدّ بعد {leftG} يومًا. جدّد الترخيص.";
            return new(LicenseStatus.Grace, false, "فترة السماح", b, Banner: JoinNotice(b, notice), DaysLeft: leftG, Org: org);
        }

        long? left = until > 0 ? DaysCeil(until - nowMs) : null;
        string warn = left != null && left <= WarnDays ? $"ينتهي الترخيص خلال {left} يومًا." : "";
        return new(LicenseStatus.Active, false, "", "", Banner: JoinNotice(warn, notice), DaysLeft: left, Org: org);
    }
}

/// <summary>مفتاح التفعيل: 20 حرفًا من أبجدية Base32 (A-Z و2-7)، يُعرض XXXX-XXXX-XXXX-XXXX-XXXX.</summary>
public static class LicenseKey
{
    /// <summary>يقبل الشرطات والمسافات وحالة الأحرف؛ null = غير صالح.</summary>
    public static string? Normalize(string? input)
    {
        if (input == null) return null;
        var s = new string(input.ToUpperInvariant().Where(c => c != '-' && !char.IsWhiteSpace(c)).ToArray());
        return s.Length == 20 && s.All(c => (c >= 'A' && c <= 'Z') || (c >= '2' && c <= '7')) ? s : null;
    }

    public static string Format(string id) =>
        string.Join("-", Enumerable.Range(0, id.Length / 4).Select(i => id.Substring(i * 4, 4)));
}

/// <summary>ترتيب الأجهزة المسجّلة على ترخيص واحد: الأقدم تسجيلًا هو المسموح.</summary>
public static class DeviceRank
{
    public sealed record Entry(string Id, long T);

    public static bool Allowed(string own, IEnumerable<Entry> entries, int max)
    {
        var sorted = entries.OrderBy(e => e.T).ThenBy(e => e.Id, StringComparer.Ordinal).ToList();
        int idx = sorted.FindIndex(e => e.Id == own);
        if (idx < 0) return false;
        return max <= 0 || idx < max;
    }
}

/// <summary>مخزن مفتاح/قيمة (ملف على ويندوز، وخريطة في الاختبارات).</summary>
public interface IKeyValue
{
    string? Get(string key);
    void Put(string key, string? value);
}
