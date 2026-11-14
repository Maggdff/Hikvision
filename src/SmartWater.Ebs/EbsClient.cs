using System.Text;
using System.Text.Json.Nodes;

namespace SmartWater.Ebs;

/// <summary>
/// عميل EBS (نقل لـ EbsClient.kt المأخوذ عن StationPC): login_inventory + Window_OperationsAndroid +
/// GetDataListDistribution(Pages). كل الحقول والمسارات كما في الأصل المؤكَّد بالتقاط شبكة؛ لا تُغيَّر بلا دليل جديد.
/// </summary>
public sealed class EbsClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _deviceUuid;
    public string? CurrentSessionId { get; private set; }

    public EbsClient(EbsSettings s, HttpClient? http = null)
    {
        _baseUrl = s.BaseUrl.EndsWith('/') ? s.BaseUrl : s.BaseUrl + "/";
        _deviceUuid = s.DeviceUuid;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    }

    public void ClearSession() => CurrentSessionId = null;

    private static JsonArray PcInfo(string appVersion) => new()
    {
        "000-000-000", "34", "Windows", "PC", appVersion, Guid.NewGuid().ToString(), "distribution",
    };

    private async Task<(int Code, bool Ok, string Body)> Post(string path, JsonNode body, CancellationToken ct)
    {
        using var content = new StringContent(EbsJson.Str(body), Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync(_baseUrl + path, content, ct).ConfigureAwait(false);
        var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ((int)resp.StatusCode, resp.IsSuccessStatusCode, text);
    }

    public async Task<EbsLoginResult> LoginAsync(EbsSettings s, CancellationToken ct = default)
    {
        try
        {
            var login = new JsonObject
            {
                ["UserName"] = s.UserName,
                ["Password"] = string.IsNullOrWhiteSpace(s.Password) ? "" : EbsCrypto.EncryptPassword(s.Password.Trim()),
                ["BranchName"] = s.BranchName, ["PublicKey"] = "", ["FileName"] = "", ["TheNumber"] = 0, ["YearID"] = 0,
                ["UserLanguageID"] = 1, ["IsClient"] = false, ["TheYear"] = s.Year, ["PCInfo"] = PcInfo("1.0"),
                ["AuthParam"] = new JsonObject { ["DeviceID"] = _deviceUuid, ["PhoneNumber"] = "" },
            };
            var (code, ok, text) = await Post("login_inventory", login, ct).ConfigureAwait(false);
            if (!ok) return new EbsLoginResult(false, HttpStatusCode: code, RawResponse: text,
                ErrorMessage: $"HTTP {code} - تحقق من الرابط/المسار، ليس بيانات الدخول.");
            if (EbsJson.TryParse(text) is not JsonObject root)
                return new EbsLoginResult(false, HttpStatusCode: code, RawResponse: text, ErrorMessage: "رد غير مفهوم من الخادم");
            int? ebsCode = root["code"] is JsonValue cv0 && int.TryParse(cv0.ToString(), out var ci0) ? ci0 : null;
            string? msg = EbsJson.Text(root, "message");
            if (ebsCode != null && ebsCode != 200)
                return new EbsLoginResult(false, HttpStatusCode: 200, RawResponse: text, ErrorMessage: $"EBS code {ebsCode}: {msg}");
            if (root["ResultBody"] == null)
                return new EbsLoginResult(false, HttpStatusCode: 200, RawResponse: text, ErrorMessage: "لا يوجد بيانات بالرد (ResultBody فارغ)");
            var body = root["ResultBody"] is JsonValue v && v.TryGetValue<string>(out var str) ? EbsJson.TryParse(str) : root["ResultBody"];
            var sid = EbsJson.Text(body, "ID");
            CurrentSessionId = sid;
            return new EbsLoginResult(!string.IsNullOrEmpty(sid), sid, EbsJson.Text(body, "FullName"), text, null, 200)
            { ErrorMessage = string.IsNullOrEmpty(sid) ? "رد الدخول بدون SessionID (حقل ID)" : null };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) { return new EbsLoginResult(false, ErrorMessage: e.Message); }
    }

    private JsonNode SessionForWire() =>
        long.TryParse(CurrentSessionId, out var n) ? JsonValue.Create(n)! : JsonValue.Create(CurrentSessionId)!;

    private static EbsOperationResult Parse(int code, bool ok, string body)
    {
        if (!ok) return new EbsOperationResult(false, code, RawResponse: body);
        if (EbsJson.TryParse(body) is not JsonObject root)
            return new EbsOperationResult(true, code, EbsMessage: "رد غير مفهوم من الخادم", RawResponse: body, EbsCode: -1);
        int? ebsCode = null;
        if (root["code"] is JsonValue cv && int.TryParse(cv.ToString(), out var ci)) ebsCode = ci;
        return new EbsOperationResult(true, code, ebsCode, EbsJson.Text(root, "message"), body, root);
    }

    private static async Task<EbsOperationResult> Safe(Func<Task<(int, bool, string)>> call, CancellationToken ct)
    {
        try { var (c, ok, t) = await call().ConfigureAwait(false); return Parse(c, ok, t); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }   // انتهاء مهلة HTTP يُعاد كفشل لا كإلغاء
        catch (Exception e) { return new EbsOperationResult(false, EbsMessage: e.Message); }
    }

    /// <summary>استدعاء عام لـ Window_OperationsAndroid (فاتورة Master/Details...).</summary>
    public Task<EbsOperationResult> CallOperationAsync(string formName, IEnumerable<JsonObject> rows, int opType = 0, long transactionId = 0, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(CurrentSessionId)) throw new InvalidOperationException("غير مسجَّل دخول — استدعِ LoginAsync أولًا.");
        var arr = new JsonArray(); foreach (var r in rows) arr.Add(r);
        var op = new JsonObject
        {
            ["FormName"] = formName, ["OpType"] = opType, ["Condition"] = "", ["IgnoreBranchCondition"] = true, ["TestDate"] = false,
            ["TransactionID"] = transactionId, ["SessionID"] = SessionForWire(), ["TheRowJson"] = EbsJson.Str(arr),
        };
        return Safe(() => Post("Window_OperationsAndroid", op, ct), ct);
    }

    public Task<EbsOperationResult> GetCountRowPagesAsync(string formName, int num, int userId, int branchId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(CurrentSessionId)) throw new InvalidOperationException("غير مسجَّل دخول — استدعِ LoginAsync أولًا.");
        JsonArray Args() => new() { num, userId, branchId };
        var op = new JsonObject
        {
            ["Condition"] = "", ["FormName"] = formName, ["IgnoreBranchCondition"] = false,
            ["Method"] = new JsonObject { ["Args"] = Args(), ["Name"] = "GetCountRowPages", ["_Args"] = Args() },
            ["OpType"] = 4, ["SessionID"] = SessionForWire(), ["TestDate"] = false, ["TheRowJson"] = "[]", ["TransactionID"] = 0,
        };
        return Safe(() => Post("Window_OperationsAndroid", op, ct), ct);
    }

    public Task<EbsOperationResult> GetDataListDistributionPagesAsync(int num, string userName, int branchId, int userId, int pageNumber, int itemsCount, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["Num"] = num.ToString(), ["UserName"] = userName, ["BranchID"] = branchId, ["UserID"] = userId,
            ["PageNumber"] = pageNumber.ToString(), ["ItemsCount"] = itemsCount.ToString(),
        };
        return Safe(() => Post("GetDataListDistributionPages", body, ct), ct);
    }

    public Task<EbsOperationResult> GetDataListDistributionAsync(int num, string userName, int branchId, int userId, int pageNumber = 0, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["Num"] = num.ToString(), ["UserName"] = userName, ["BranchID"] = branchId, ["PageNumber"] = pageNumber, ["UserID"] = userId,
        };
        return Safe(() => Post("GetDataListDistribution", body, ct), ct);
    }
}
