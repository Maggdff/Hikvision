using System.Net;
using System.Text.Json;
using SmartWater.Licensing;
using Xunit;

namespace SmartWater.Tests;

internal sealed class MemKv : IKeyValue
{
    private readonly Dictionary<string, string> _d = new();
    public string? Get(string key) => _d.TryGetValue(key, out var v) ? v : null;
    public void Put(string key, string? value) { if (value == null) _d.Remove(key); else _d[key] = value; }
}

public class LicenseCodecTests
{
    [Fact]
    public void Roundtrip_preserves_all_fields_including_special_characters()
    {
        var d = new LicenseDoc(9, "بمب|الأمل%1", true, 1000, 2000, 7, true, "سطر1\nسطر2", "ملاحظة|x", "سبب\r%", 3, 2, 5000);
        var back = LicenseCodec.Decode(LicenseCodec.Encode(d));
        Assert.Equal(d, back);
    }

    [Fact]
    public void Decodes_literal_string_in_the_android_and_console_format()
    {
        var d = LicenseCodec.Decode("SWL1|4|Test|A|1000|9000|14|0||||0|0|2000");
        Assert.NotNull(d);
        Assert.Equal("Test", d!.Org);
        Assert.True(d.Active);
        Assert.Equal(14, d.GraceDays);
        Assert.Equal(9000, d.ValidUntilMs);
    }

    [Fact]
    public void Malformed_texts_decode_to_null()
    {
        const string raw = "SWL1|4|Test|A|1000|9000|14|0||||0|0|2000";
        var bad = new string?[]
        {
            null, "", "UAL1|1|x|A|0|0|14|0|||0|0|0", raw.Replace("SWL1", "XXXX"), raw + "|1",
            raw.Replace("|A|", "|Z|"), raw.Replace("|4|", "|-4|"), raw.Replace("|0|0|2000", "|0|x|2000"),
            raw.Replace("Test", "bad%ZZ"),
        };
        foreach (var t in bad) Assert.Null(LicenseCodec.Decode(t));
    }
}

public class LicenseGateTests
{
    private const long Day = LicenseGate.DayMs;
    private const long Now = 100 * Day;
    private static LicenseDoc D(long until, bool active = true, int grace = 7, int offline = 0, int maxDev = 0, bool maint = false, string notice = "", string reason = "") =>
        new(1, "x", active, 0, until, grace, maint, "", notice, reason, offline, maxDev, 0);

    [Fact] public void Not_activated_is_blocked() => Assert.Equal(LicenseStatus.NotActivated, LicenseGate.Evaluate(null, Now).Status);
    [Fact] public void Revoked_wins() => Assert.Equal(LicenseStatus.Revoked, LicenseGate.Evaluate(D(0), Now, revoked: true).Status);

    [Fact]
    public void Active_without_end_and_with_far_end()
    {
        Assert.False(LicenseGate.Evaluate(D(0), Now).Blocked);
        var v = LicenseGate.Evaluate(D(Now + 60 * Day), Now);
        Assert.Equal(LicenseStatus.Active, v.Status); Assert.Equal(60L, v.DaysLeft); Assert.Equal("", v.Banner);
    }

    [Fact]
    public void Warns_within_14_days()
    {
        var v = LicenseGate.Evaluate(D(Now + 10 * Day), Now);
        Assert.Equal(LicenseStatus.Active, v.Status); Assert.Contains("10", v.Banner);
    }

    [Fact]
    public void Grace_works_then_expires()
    {
        var g = LicenseGate.Evaluate(D(Now - 2 * Day, grace: 7), Now);
        Assert.Equal(LicenseStatus.Grace, g.Status); Assert.False(g.Blocked);
        var e = LicenseGate.Evaluate(D(Now - 8 * Day, grace: 7), Now);
        Assert.Equal(LicenseStatus.Expired, e.Status); Assert.True(e.Blocked);
    }

    [Fact]
    public void Suspended_shows_reason()
    {
        var v = LicenseGate.Evaluate(D(0, active: false, reason: "لم يُسدَّد الاشتراك"), Now);
        Assert.Equal(LicenseStatus.Suspended, v.Status); Assert.Contains("لم يُسدَّد الاشتراك", v.Message);
    }

    [Fact]
    public void Maintenance_device_limit_and_offline_limit_block()
    {
        Assert.Equal(LicenseStatus.Maintenance, LicenseGate.Evaluate(D(0, maint: true), Now).Status);
        Assert.Equal(LicenseStatus.DeviceLimit, LicenseGate.Evaluate(D(0, maxDev: 1), Now, deviceOk: false).Status);
        Assert.Equal(LicenseStatus.OfflineLimit, LicenseGate.Evaluate(D(0, offline: 3), Now, lastContactMs: Now - 4 * Day).Status);
        Assert.False(LicenseGate.Evaluate(D(0, offline: 3), Now, lastContactMs: Now - 2 * Day).Blocked);
    }
}

public class LicenseKeyAndRankTests
{
    [Fact]
    public void Key_normalizes_dashes_spaces_and_case()
    {
        Assert.Equal("J54NL3MTJBGB7WUT4S5A", LicenseKey.Normalize("j54n-l3mt jbgb-7wut-4s5a"));
        Assert.Equal("J54N-L3MT-JBGB-7WUT-4S5A", LicenseKey.Format("J54NL3MTJBGB7WUT4S5A"));
        Assert.Null(LicenseKey.Normalize("J54N-L3MT-JBGB-7WUT-4S51"));   // 1 خارج الأبجدية
        Assert.Null(LicenseKey.Normalize("SHORT"));
        Assert.Null(LicenseKey.Normalize(null));
    }

    [Fact]
    public void Oldest_registered_devices_are_the_allowed_ones()
    {
        var e = new[] { new DeviceRank.Entry("B", 20), new DeviceRank.Entry("A", 10), new DeviceRank.Entry("C", 30) };
        Assert.True(DeviceRank.Allowed("A", e, 2));
        Assert.True(DeviceRank.Allowed("B", e, 2));
        Assert.False(DeviceRank.Allowed("C", e, 2));
        Assert.True(DeviceRank.Allowed("C", e, 0));          // بلا حد
        Assert.False(DeviceRank.Allowed("Z", e, 0));         // غير مسجّل
    }
}

public class LicenseKeeperTests
{
    private const long T0 = 1_800_000_000_000L;
    private long _wall = T0, _mono = 5_000;
    private LicenseKeeper NewKeeper(IKeyValue kv) => new(kv, () => _wall, () => _mono);
    private static string Raw(long until, int offline = 0) =>
        LicenseCodec.Encode(new LicenseDoc(1, "x", true, T0, until, 0, false, "", "", "", offline, 0, T0));

    [Fact]
    public void Moving_the_pc_clock_forward_or_back_does_not_change_the_verdict()
    {
        var kv = new MemKv(); var k = NewKeeper(kv);
        k.SetLicenseId("J54NL3MTJBGB7WUT4S5A");
        k.OnFetched(Raw(T0 + 5 * LicenseGate.DayMs), T0);
        Assert.Equal(LicenseStatus.Active, k.Verdict().Status);
        _wall += 400 * LicenseGate.DayMs;                     // تقديم ساعة الجهاز سنة+
        Assert.Equal(LicenseStatus.Active, k.Verdict().Status);
        _wall -= 800 * LicenseGate.DayMs;                     // وتأخيرها
        Assert.Equal(LicenseStatus.Active, k.Verdict().Status);
        _mono += 6 * LicenseGate.DayMs;                       // مرور وقت حقيقي (ساعة رتيبة)
        Assert.Equal(LicenseStatus.Expired, k.Verdict().Status);
    }

    [Fact]
    public void Missing_document_on_server_means_revoked_and_corrupt_text_keeps_last_good()
    {
        var k = NewKeeper(new MemKv());
        k.SetLicenseId("J54NL3MTJBGB7WUT4S5A");
        k.OnFetched(Raw(0), T0);
        k.OnFetched("garbage", T0 + 1);
        Assert.Equal(LicenseStatus.Active, k.Verdict().Status);
        k.OnFetched(null, T0 + 2);
        Assert.Equal(LicenseStatus.Revoked, k.Verdict().Status);
    }

    [Fact]
    public void State_survives_restart_while_clock_stays_trusted()
    {
        var kv = new MemKv();
        var k1 = NewKeeper(kv);
        k1.SetLicenseId("J54NL3MTJBGB7WUT4S5A");
        k1.OnFetched(Raw(T0 + 5 * LicenseGate.DayMs), T0);
        var k2 = NewKeeper(kv);
        Assert.Equal(LicenseStatus.Active, k2.Verdict().Status);
    }

    [Fact]
    public void Unmanaged_build_runs_without_a_key()
    {
        var k = NewKeeper(new MemKv());
        Assert.False(k.Verdict(required: false).Blocked);
        Assert.True(k.Verdict(required: true).Blocked);
    }
}

public class LicenseApiTests
{
    private sealed class FakeFirebase : HttpMessageHandler
    {
        public string? Lic; public Dictionary<string, long> Bind = new(); public long Clock = 1_800_000_000_000L; public int Puts;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var path = req.RequestUri!.AbsolutePath;
            HttpResponseMessage Ok(string body)
            {
                var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
                r.Headers.Date = DateTimeOffset.FromUnixTimeMilliseconds(Clock);
                return r;
            }
            if (req.Method == HttpMethod.Get && path.StartsWith("/swm/lic/")) return Task.FromResult(Ok(Lic == null ? "null" : JsonSerializer.Serialize(Lic)));
            if (req.Method == HttpMethod.Get && path.StartsWith("/swm/bind/"))
                return Task.FromResult(Ok(Bind.Count == 0 ? "null" : "{" + string.Join(",", Bind.Select(kv => $"\"{kv.Key}\":{{\"t\":{kv.Value},\"n\":\"pc\"}}")) + "}"));
            if (req.Method == HttpMethod.Put && path.StartsWith("/swm/bind/"))
            {
                var dev = path[(path.LastIndexOf('/') + 1)..].Replace(".json", "");
                if (!Bind.ContainsKey(dev)) Bind[dev] = Clock + Bind.Count;
                Puts++;
                return Task.FromResult(Ok("{}"));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private const string Key = "J54NL3MTJBGB7WUT4S5A";
    private const string Base = "https://example-rtdb.firebaseio.com";

    private static string Raw(int maxDev) =>
        LicenseCodec.Encode(new LicenseDoc(1, "بمب", true, 0, 0, 0, false, "", "", "", 0, maxDev, 0));

    [Fact]
    public async Task Registers_the_device_once_and_reports_allowed()
    {
        var fb = new FakeFirebase { Lic = Raw(1) };
        var api = new LicenseApi(new HttpClient(fb));
        var r1 = Assert.IsType<LicenseApi.Result.Ok>(await api.SyncAsync(Base, Key, "AAAAAAAA11111111", "pc1"));
        Assert.True(r1.Sync.DeviceOk); Assert.True(r1.Sync.Registered); Assert.Equal(fb.Clock, r1.Sync.ServerMs);
        var r2 = Assert.IsType<LicenseApi.Result.Ok>(await api.SyncAsync(Base, Key, "AAAAAAAA11111111", "pc1"));
        Assert.True(r2.Sync.DeviceOk);
        Assert.Equal(1, fb.Puts);                                   // لا تسجيل مكرر
    }

    [Fact]
    public async Task Second_device_is_over_the_limit()
    {
        var fb = new FakeFirebase { Lic = Raw(1) };
        var api = new LicenseApi(new HttpClient(fb));
        await api.SyncAsync(Base, Key, "AAAAAAAA11111111", "pc1");
        var r = Assert.IsType<LicenseApi.Result.Ok>(await api.SyncAsync(Base, Key, "BBBBBBBB22222222", "pc2"));
        Assert.False(r.Sync.DeviceOk);
    }

    [Fact]
    public async Task Missing_license_returns_null_raw_and_bad_inputs_are_errors()
    {
        var api = new LicenseApi(new HttpClient(new FakeFirebase { Lic = null }));
        var r = Assert.IsType<LicenseApi.Result.Ok>(await api.SyncAsync(Base, Key, "AAAAAAAA11111111", "pc1"));
        Assert.Null(r.Sync.Raw);
        Assert.IsType<LicenseApi.Result.Error>(await api.SyncAsync("ftp://x", Key, "AAAAAAAA11111111", "pc1"));
        Assert.IsType<LicenseApi.Result.Error>(await api.SyncAsync(Base, "bad", "AAAAAAAA11111111", "pc1"));
        Assert.IsType<LicenseApi.Result.Error>(await api.SyncAsync(Base, Key, "x!", "pc1"));
    }

    [Fact]
    public async Task Manager_activates_with_a_valid_key_and_rejects_unknown_key()
    {
        var fb = new FakeFirebase { Lic = Raw(0) };
        var mgr = new LicenseManager(new MemKv(), new LicenseOptions { LicenseUrl = Base }, new LicenseApi(new HttpClient(fb)));
        Assert.True(mgr.Verdict().Blocked);                         // قبل التفعيل
        Assert.Null(await mgr.ActivateAsync("J54N-L3MT-JBGB-7WUT-4S5A"));
        Assert.True(mgr.Allowed);
        fb.Lic = null;
        await mgr.RefreshAsync(force: true);
        Assert.Equal(LicenseStatus.Revoked, mgr.Verdict().Status);  // حُذف من الخادم
        var mgr2 = new LicenseManager(new MemKv(), new LicenseOptions { LicenseUrl = Base }, new LicenseApi(new HttpClient(fb)));
        Assert.Contains("غير موجود", await mgr2.ActivateAsync(Key) ?? "");
    }
}
