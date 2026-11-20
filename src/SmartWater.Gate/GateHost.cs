using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace SmartWater.Gate;

/// <summary>
/// مضيف HTTP للأجهزة (Kestrel داخل التطبيق). كل طلب موقَّع بـ HMAC (ترويسات X-Device/X-Seq/X-Ts/X-Nonce/X-Sig).
/// المسارات: /api/device/heartbeat، /api/access/check، /api/access/ack، /api/device/events. لا واجهة إدارة هنا.
/// </summary>
public static class GateHost
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

    public static async Task<WebApplication> StartAsync(GateService svc, string url)
    {
        var b = WebApplication.CreateSlimBuilder();
        b.WebHost.UseUrls(url);
        b.Logging.ClearProviders();
        var app = b.Build();

        app.MapPost("/api/device/heartbeat", ctx => Handle(ctx, svc, (dev, j) =>
            Task.FromResult<object>(svc.Heartbeat(dev, State(j, "lockState"), Bool(j, "readerOk"), Str(j, "fw")))));

        app.MapPost("/api/access/check", ctx => Handle(ctx, svc, async (dev, j) =>
            (object)await svc.CheckAsync(dev, Str(j, "uid"), ctx.RequestAborted).ConfigureAwait(false)));

        app.MapPost("/api/access/ack", ctx => Handle(ctx, svc, (dev, j) =>
            Task.FromResult<object>(svc.Ack(dev, Str(j, "cmdId"), Bool(j, "executed"), State(j, "lockState")))));

        app.MapPost("/api/device/events", ctx => Handle(ctx, svc, (dev, j) =>
        {
            var list = new List<(long, long, string, string)>();
            if (j.TryGetProperty("events", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var e in arr.EnumerateArray())
                    list.Add((Lng(e, "seq"), Lng(e, "ts"), Str(e, "type"), Str(e, "data")));
            return Task.FromResult<object>(new { accepted = svc.UploadEvents(dev, list.Select(x => (x.Item1, x.Item2, x.Item3, x.Item4))) });
        }));

        await app.StartAsync().ConfigureAwait(false);
        return app;
    }

    private static string Str(JsonElement j, string n) => j.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static bool Bool(JsonElement j, string n) => j.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
    private static long Lng(JsonElement j, string n) => j.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l) ? l : 0L;
    private static LockState State(JsonElement j, string n) => Enum.TryParse<LockState>(Str(j, n), true, out var s) ? s : LockState.Unknown;

    private static async Task Handle(HttpContext ctx, GateService svc, Func<string, JsonElement, Task<object>> action)
    {
        byte[] body;
        using (var ms = new MemoryStream()) { await ctx.Request.Body.CopyToAsync(ms).ConfigureAwait(false); body = ms.ToArray(); }
        var h = ctx.Request.Headers;
        string dev = h["X-Device"].ToString();
        if (!long.TryParse(h["X-Seq"].ToString(), out var seq) || !long.TryParse(h["X-Ts"].ToString(), out var ts))
        { ctx.Response.StatusCode = 400; return; }
        var st = svc.Authenticate(dev, seq, ts, h["X-Nonce"].ToString(), h["X-Sig"].ToString(), ctx.Request.Method, ctx.Request.Path.Value ?? "", body);
        if (st != AuthStatus.Ok)
        {
            ctx.Response.StatusCode = 401;
            await ctx.Response.WriteAsJsonAsync(new { error = st.ToString(), serverTimeMs = svc.ServerNowMs }, Json).ConfigureAwait(false);
            return;
        }
        JsonDocument doc;
        try { doc = JsonDocument.Parse(body.Length == 0 ? "{}"u8.ToArray() : body); }
        catch (JsonException) { ctx.Response.StatusCode = 400; return; }
        using (doc)
        {
            var res = await action(dev, doc.RootElement).ConfigureAwait(false);
            await ctx.Response.WriteAsJsonAsync(res, res.GetType(), Json).ConfigureAwait(false);
        }
    }
}
