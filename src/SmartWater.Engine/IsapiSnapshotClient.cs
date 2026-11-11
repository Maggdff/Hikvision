using System.Net;

namespace SmartWater.Engine;

/// <summary>
/// لقطة JPEG من جهاز Hikvision عبر ISAPI/HTTP (بدون SDK): GET /ISAPI/Streaming/channels/{قناة*100+بث}/picture
/// مصادقة Digest (أو Basic). القناة 4 بث رئيسي = 401، والقناة 7 = 701.
/// </summary>
public sealed class IsapiSnapshotClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly Uri _baseUri;

    public IsapiSnapshotClient(string host, int port, string user, string password, bool https = false, TimeSpan? timeout = null)
    {
        _baseUri = new Uri($"{(https ? "https" : "http")}://{host}:{port}/");
        var cache = new CredentialCache();
        var cred = new NetworkCredential(user, password);
        cache.Add(_baseUri, "Digest", cred);
        cache.Add(_baseUri, "Basic", cred);
        var handler = new HttpClientHandler { Credentials = cache, PreAuthenticate = false };
        _http = new HttpClient(handler) { Timeout = timeout ?? TimeSpan.FromSeconds(8) };
    }

    public string LastError { get; private set; } = "";

    /// <summary>يعيد بايتات JPEG أو null عند الفشل (السبب في LastError).</summary>
    public async Task<byte[]?> GetJpegAsync(int channel, int stream = 1, CancellationToken ct = default)
    {
        try
        {
            var uri = new Uri(_baseUri, $"ISAPI/Streaming/channels/{channel * 100 + stream}/picture");
            using var resp = await _http.GetAsync(uri, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                LastError = $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}";
                return null;
            }
            var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8)
            {
                LastError = "الرد ليس JPEG";
                return null;
            }
            LastError = "";
            return bytes;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) { LastError = e.Message; return null; }
    }

    public void Dispose() => _http.Dispose();
}
