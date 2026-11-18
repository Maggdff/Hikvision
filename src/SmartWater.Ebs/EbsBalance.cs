using System.Text.Json.Nodes;

namespace SmartWater.Ebs;

/// <summary>قراءة رصيد حساب من EBS: Madeen = مدين، Dain = دائن. الرصيد الصافي للعميل (موجب = له رصيد) = Dain − Madeen.</summary>
public sealed record AccountReading(bool Ok, double Madeen, double Dain, string Currency, string State, bool Empty, string? Error)
{
    public double Credit => Dain - Madeen;
}

public static class EbsBalanceParser
{
    /// <summary>يفسّر رد One_Account (نقل لـ tryReadAccount). عند عدّة صفوف يُفضَّل صف العملة المطلوبة وإلا الأول.</summary>
    public static AccountReading Parse(EbsOperationResult op, string? preferredCurrency = null)
    {
        if (!op.HttpSuccess) return new AccountReading(false, 0, 0, "", "", false, $"خطأ اتصال HTTP {op.HttpStatusCode} {op.EbsMessage}".Trim());
        if (op.EbsCode != null && op.EbsCode != 200) return new AccountReading(false, 0, 0, "", "", false, $"EBS: {op.EbsMessage ?? op.EbsCode.ToString()}");
        var arr = EbsJson.AsArray(op.Root?["ResultBody"]);
        if (arr == null || arr.Count == 0) return new AccountReading(true, 0, 0, preferredCurrency ?? "", "", true, null);
        JsonNode? pick = arr[0];
        if (!string.IsNullOrWhiteSpace(preferredCurrency))
            foreach (var n in arr)
                if (n != null && string.Equals(EbsJson.Text(n, "CurrencyName")?.Trim(), preferredCurrency.Trim(), StringComparison.OrdinalIgnoreCase)) { pick = n; break; }
        double Clean(double v) => double.IsNaN(v) ? 0.0 : v;
        return new AccountReading(true, Clean(EbsJson.Num(pick, "Madeen")), Clean(EbsJson.Num(pick, "Dain")),
            EbsJson.Text(pick, "CurrencyName") ?? "", EbsJson.Text(pick, "BalanceState") ?? "", false, null);
    }
}
