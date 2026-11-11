using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SmartWater.Licensing;

/// <summary>
/// جلب الترخيص وتسجيل الجهاز من Firebase Realtime Database عبر REST (بلا SDK). نفس مسارات نسخة أندرويد:
///  swm/lic/{مفتاح}: مستند الترخيص (قراءة بالمفتاح فقط).
///  swm/bind/{مفتاح}/{جهاز}: تسجيل الجهاز (يُكتب مرة واحدة؛ ختم الزمن من الخادم).
/// وقت الخادم من ترويسة Date. أي خطأ لا يغيّر الحكم المحفوظ.
/// </summary>
public sealed class LicenseApi
{
    public const string Root = "swm";
    private static readonly Regex DeviceIdRx = new(@"^[A-Za-z0-9]{8,40}\z", RegexOptions.Compiled);

    public sealed record SyncResult(string? Raw, long ServerMs, bool DeviceOk, bool Registered);

    public abstract record Result
    {
        public sealed record Ok(SyncResult Sync) : Result;
        public sealed record Offline : Result;
        public sealed record Error(string Msg) : Result;
    }

    private sealed record Resp(int Code, string Body, long DateMs);
    private sealed class ApiError : Exception { public ApiError(string msg) : base(msg) { } }

    private readonly HttpClient _http;
    public LicenseApi(HttpClient? http = null) => _http = http ?? new HttpClient();

    private async Task<Resp> Send(HttpMethod method, string url, string? body, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        using var req = new HttpRequestMessage(method, url);
        if (body != null) req.Content = new StringContent(body, Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
        using var resp = await _http.SendAsync(req, cts.Token).ConfigureAwait(false);
        string text = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
        long dateMs = resp.Headers.Date?.ToUnixTimeMilliseconds() ?? 0L;
        return new Resp((int)resp.StatusCode, text, dateMs);
    }

    private static string Friendly(int code) => code switch
    {
        401 or 403 => "قواعد قاعدة الترخيص لم تُنشر بعد (Permission denied)",
        404 => "عنوان قاعدة الترخيص غير صحيح",
        429 => "طلبات كثيرة؛ أعد المحاولة بعد قليل",
        _ => $"خطأ من خادم الترخيص ({code})",
    };

    private static List<DeviceRank.Entry> ParseBindings(string body)
    {
        var t = body.Trim();
        var list = new List<DeviceRank.Entry>();
        if (t.Length == 0 || t == "null") return list;
        using var doc = JsonDocument.Parse(t);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return list;
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Object
                && prop.Value.TryGetProperty("t", out var tv) && tv.ValueKind == JsonValueKind.Number && tv.TryGetInt64(out var tm))
                list.Add(new DeviceRank.Entry(prop.Name, tm));
        }
        return list;
    }

    /// <summary>يجلب المستند ويسجّل هذا الجهاز (إن لم يُسجَّل) ويحسب هل هو ضمن العدد المسموح.</summary>
    public async Task<Result> SyncAsync(string baseUrl, string licenseId, string deviceId, string deviceName,
                                        TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var to = timeout ?? TimeSpan.FromSeconds(12);
        var b = baseUrl.Trim().TrimEnd('/');
        if (!(b.StartsWith("https://") || b.StartsWith("http://"))) return new Result.Error("عنوان قاعدة الترخيص غير صالح");
        var id = LicenseKey.Normalize(licenseId);
        if (id == null) return new Result.Error("مفتاح التفعيل غير صالح");
        if (!DeviceIdRx.IsMatch(deviceId)) return new Result.Error("معرّف الجهاز غير صالح");
        try
        {
            var d = await Send(HttpMethod.Get, $"{b}/{Root}/lic/{id}.json", null, to, ct).ConfigureAwait(false);
            if (d.Code is < 200 or > 299) return new Result.Error(Friendly(d.Code));
            if (d.DateMs <= 0L) return new Result.Error("لم يرسل الخادم وقته");
            var txt = d.Body.Trim();
            string? raw = null;
            if (txt.Length != 0 && txt != "null")
            {
                using var doc = JsonDocument.Parse(txt);
                raw = doc.RootElement.ValueKind == JsonValueKind.String ? doc.RootElement.GetString() : null;
            }
            if (raw == null) return new Result.Ok(new SyncResult(null, d.DateMs, true, false));

            int max = LicenseCodec.Decode(raw)?.MaxDevices ?? 0;
            var entries = await FetchBindings(b, id, to, ct).ConfigureAwait(false);
            bool registered = entries.Any(e => e.Id == deviceId);
            if (!registered)
            {
                string body = "{\"t\":{\".sv\":\"timestamp\"},\"n\":" + JsonSerializer.Serialize(deviceName.Length > 50 ? deviceName[..50] : deviceName) + "}";
                var r = await Send(HttpMethod.Put, $"{b}/{Root}/bind/{id}/{deviceId}.json", body, to, ct).ConfigureAwait(false);
                // 401/403 هنا = مسجَّل مسبقًا (القاعدة write-once) أو لا صلاحية؛ نعيد القراءة لنعرف
                if (r.Code is (< 200 or > 299) and not 401 and not 403) return new Result.Error(Friendly(r.Code));
                entries = await FetchBindings(b, id, to, ct).ConfigureAwait(false);
                registered = entries.Any(e => e.Id == deviceId);
            }
            bool ok = registered ? DeviceRank.Allowed(deviceId, entries, max) : max <= 0;
            return new Result.Ok(new SyncResult(raw, d.DateMs, ok, registered));
        }
        catch (ApiError e) { return new Result.Error(e.Message); }
        catch (JsonException) { return new Result.Error("استجابة غير مفهومة من الخادم"); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return new Result.Offline(); }   // انتهت المهلة
        catch (HttpRequestException) { return new Result.Offline(); }
        catch (IOException) { return new Result.Offline(); }
    }

    private async Task<List<DeviceRank.Entry>> FetchBindings(string b, string id, TimeSpan to, CancellationToken ct)
    {
        var r = await Send(HttpMethod.Get, $"{b}/{Root}/bind/{id}.json", null, to, ct).ConfigureAwait(false);
        if (r.Code is < 200 or > 299) throw new ApiError(Friendly(r.Code));
        return ParseBindings(r.Body);
    }
}
