using System.Text.Json.Nodes;

namespace SmartWater.Ebs;

public sealed record CustomerDownloadResult(bool Success, int Count, int BalanceCount = 0, string? Error = null);

/// <summary>
/// تحميل عملاء EBS (Num=1) وأرصدتهم (Num=11) وعملاتهم (Num=2): GetCountRowPages ثم GetDataListDistributionPages صفحات 200.
/// نقل لـ CustomerDownloadService.kt. فشل الأرصدة أو العملات وحده لا يُفشل العملية (العملاء أهم جزء).
/// </summary>
public sealed class CustomerDownloadService
{
    private const string DistributionInfoForm = "EBS.Mobile.Distribution.wndInv_DistributionInfo";
    private const int CustomersNum = 1, BalancesNum = 11, CurrenciesNum = 2, PageSize = 200;

    private readonly EbsDb _db;
    private readonly EbsClient _client;
    private string? _lastError;

    public CustomerDownloadService(EbsDb db, EbsClient client) { _db = db; _client = client; }

    private async Task<bool> EnsureLoggedIn(EbsSettings s, CancellationToken ct) =>
        !string.IsNullOrEmpty(_client.CurrentSessionId) || (await _client.LoginAsync(s, ct).ConfigureAwait(false)).Success;

    public async Task<CustomerDownloadResult> DownloadAsync(EbsSettings s, CancellationToken ct = default)
    {
        if (!await EnsureLoggedIn(s, ct).ConfigureAwait(false))
            return new CustomerDownloadResult(false, 0, Error: "فشل تسجيل الدخول لـEBS");
        if (s.UserId <= 0 || s.BranchId <= 0)
            return new CustomerDownloadResult(false, 0, Error: "أدخل «رقم المستخدم» و«رقم الفرع» الرقميين في الإعدادات أولًا (مختلفان عن اسم المستخدم/الفرع).");
        var userName = string.IsNullOrWhiteSpace(s.CashierDisplayName) ? s.UserName : s.CashierDisplayName;

        var rows = await PagedRows(CustomersNum, s, userName, ct).ConfigureAwait(false);
        if (rows == null) return new CustomerDownloadResult(false, 0, Error: _lastError);
        var customers = rows.Select(ParseCustomer).Where(c => c != null).Cast<EbsCustomer>().ToList();
        _db.ReplaceCustomers(customers);

        var cur = await _client.GetDataListDistributionAsync(CurrenciesNum, userName, s.BranchId, s.UserId, ct: ct).ConfigureAwait(false);
        if (cur.IsFullSuccess && EbsJson.AsArray(cur.Root?["ResultBody"]) is { } carr)
        {
            var list = new List<(long, string)>();
            foreach (var n in carr)
            {
                var name = EbsJson.Text(n, "CurrencyName")?.Trim();
                long id = (long)EbsJson.Num(n, "ID");
                if (!string.IsNullOrEmpty(name) && id != 0) list.Add((id, name));
            }
            _db.ReplaceCurrencies(list);
        }

        int balCount = 0;
        var brows = await PagedRows(BalancesNum, s, userName, ct).ConfigureAwait(false);
        if (brows != null)
        {
            var bals = brows.Select(n => new EbsBalance((long)EbsJson.Num(n, "AccountID"), EbsJson.Num(n, "Amount"), (long)EbsJson.Num(n, "CurrencyID")))
                            .Where(b => b.AccountId != 0).ToList();
            _db.ReplaceBalances(bals);
            balCount = bals.Count;
        }
        return new CustomerDownloadResult(true, customers.Count, balCount);
    }

    private async Task<List<JsonNode>?> PagedRows(int num, EbsSettings s, string userName, CancellationToken ct)
    {
        var count = await _client.GetCountRowPagesAsync(DistributionInfoForm, num, s.UserId, s.BranchId, ct).ConfigureAwait(false);
        if (!count.IsFullSuccess) { _lastError = Describe(count); return null; }
        int? total = null;
        if (EbsJson.AsArray(count.Root?["ResultBody"]) is { Count: > 0 } ca && ca[0] is JsonObject co && co.ContainsKey("count"))
            total = (int)EbsJson.Num(co, "count");
        int pages = total is null or <= 0 ? 1 : ((total.Value - 1) / PageSize) + 1;
        var all = new List<JsonNode>();
        for (int p = 0; p < pages; p++)
        {
            var r = await _client.GetDataListDistributionPagesAsync(num, userName, s.BranchId, s.UserId, p, PageSize, ct).ConfigureAwait(false);
            if (!r.IsFullSuccess) { _lastError = Describe(r); return null; }
            var arr = EbsJson.AsArray(r.Root?["ResultBody"]);
            if (arr != null) foreach (var n in arr) if (n != null) all.Add(n);
        }
        return all;
    }

    private static EbsCustomer? ParseCustomer(JsonNode n)
    {
        var name = EbsJson.Text(n, "TheName")?.Trim();
        if (string.IsNullOrEmpty(name)) return null;
        long id = (long)EbsJson.Num(n, "ID");
        long acc = (long)EbsJson.Num(n, "AccountID");
        string? Blank(string? x) => string.IsNullOrWhiteSpace(x) ? null : x;
        return new EbsCustomer(id != 0 ? id : StableId(name), name, Blank(EbsJson.Text(n, "Mobile")), Blank(EbsJson.Text(n, "Address")), acc != 0 ? acc : null);
    }

    /// <summary>معرّف ثابت (غير عشوائي بين التشغيلات) لاسم بلا ID: FNV-1a سالب كي لا يتعارض مع معرّفات السيرفر.</summary>
    private static long StableId(string name)
    {
        ulong h = 1469598103934665603UL;
        foreach (var ch in name) { h ^= ch; h *= 1099511628211UL; }
        return -(long)(h >> 2) - 1;
    }

    private static string Describe(EbsOperationResult r)
    {
        if (!r.HttpSuccess) return $"HTTP {r.HttpStatusCode} (تحقق من الرابط/المسار)";
        if (r.EbsCode != null && r.EbsCode != 200) return $"EBS code {r.EbsCode}: {r.EbsMessage}";
        return "فشل غير معروف";
    }
}
