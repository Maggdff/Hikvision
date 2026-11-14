using System.Globalization;
using SmartWater.Ebs;

namespace SmartWater.App;

public sealed record RecordRow(long Id, int Seq, string Day, string Type, string Meters, string Start, string End,
                               string M1, string M2, string Total, string Measured, string Truck, string EbsStatus, string Notes, long? TruckId);

public sealed record TruckRow(long Id, string Name, string Capacity, int Photos, double CapacityValue);

public sealed record TruckChoice(long Id, string Label);

public sealed record InvRow(string LocalId, string Customer, string Barrels, string Price, string Total, string Status, int Attempts, string Error);

public static class Ui
{
    /// <summary>يقبل الأرقام العربية-الهندية وفاصلة عربية: «٥٥٫٥» → 55.5</summary>
    public static bool TryNum(string? s, out double v)
    {
        v = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var t = new string(s.Trim().Select(c => c >= '٠' && c <= '٩' ? (char)('0' + (c - '٠')) : c == '٫' || c == ',' || c == '،' ? '.' : c).ToArray());
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }

    public static string InvStatus(InvoiceStatus s) => s switch
    {
        InvoiceStatus.LocalCreated => "بانتظار الإرسال",
        InvoiceStatus.Syncing => "جارٍ الإرسال",
        InvoiceStatus.MasterSuccess => "أُرسل رأس الفاتورة",
        InvoiceStatus.DetailsPending => "بانتظار التفاصيل",
        InvoiceStatus.DetailsSuccess => "أُرسلت التفاصيل",
        InvoiceStatus.Synced => "تمت المزامنة",
        InvoiceStatus.SyncFailed => "فشلت",
        InvoiceStatus.NeedsConfirm => "يحتاج تأكيد (سجل مكرر)",
        _ => s.ToString(),
    };

    public static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    public static string Time(long ms) => ms <= 0 ? "" : DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
}
