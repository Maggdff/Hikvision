using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using SmartWater.Data;
using SmartWater.Ebs;
using Xunit;

namespace SmartWater.Tests;

internal sealed class FakeEbs : HttpMessageHandler
{
    public bool FailLogin, DuplicateOnce, DetailsFails, ThrowNetwork;
    public int Customers = 450, MasterCalls, DetailsCalls, PageCalls, LoginCalls;
    public int NextMasterId = 101, NextDetailsId = 501;
    public JsonObject? LastMaster, LastDetails;
    public List<string> MasterNumbers = new();

    private static HttpResponseMessage Json(string body, HttpStatusCode c = HttpStatusCode.OK) =>
        new(c) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        if (ThrowNetwork) throw new HttpRequestException("network down");
        var path = req.RequestUri!.AbsolutePath;
        var body = JsonNode.Parse(await req.Content!.ReadAsStringAsync(ct))!.AsObject();
        if (path.EndsWith("login_inventory"))
        {
            LoginCalls++;
            if (FailLogin) return Json("{\"code\":401,\"message\":\"كلمة المرور خاطئة\"}");
            return Json("{\"code\":200,\"ResultBody\":{\"ID\":22795,\"FullName\":\"موزع\"}}");
        }
        if (path.EndsWith("Window_OperationsAndroid"))
        {
            Assert.True(body["SessionID"] is JsonValue);                                // رقم صرف لا نص
            Assert.Equal(22795L, body["SessionID"]!.GetValue<long>());
            var method = body["Method"]?["Name"]?.GetValue<string>();
            if (method == "GetCountRowPages")
            {
                int num = body["Method"]!["Args"]![0]!.GetValue<int>();
                return Json("{\"code\":200,\"ResultBody\":[{\"count\":" + (num == 1 ? Customers : 3) + "}]}");
            }
            var form = body["FormName"]!.GetValue<string>();
            var row = JsonNode.Parse(body["TheRowJson"]!.GetValue<string>())!.AsArray()[0]!.AsObject();
            if (form.EndsWith("wndInv_SellInvoice"))
            {
                MasterCalls++; LastMaster = row; MasterNumbers.Add(row["TheNumberFromApp"]!.ToString());
                if (DuplicateOnce) { DuplicateOnce = false; return Json("{\"code\":500,\"message\":\"يوجد سجل له نفس بيانات هذا السجل\"}"); }
                // ResultBody نص يحوي JSON مُرمَّز (كما يفعل السيرفر الحقيقي)
                return Json("{\"code\":200,\"message\":\"\",\"ResultBody\":\"[{\\\"ID\\\":" + NextMasterId++ + "}]\"}");
            }
            if (form.EndsWith("wndInv_SellInvoiceDetails"))
            {
                DetailsCalls++; LastDetails = row;
                if (DetailsFails) return Json("{\"code\":500,\"message\":\"خطأ بالتفاصيل\"}");
                return Json("{\"code\":200,\"ResultBody\":\"{\\\"ID\\\":" + NextDetailsId++ + "}\"}");
            }
        }
        if (path.EndsWith("GetDataListDistributionPages"))
        {
            PageCalls++;
            int num = int.Parse(body["Num"]!.GetValue<string>()), page = int.Parse(body["PageNumber"]!.GetValue<string>());
            var arr = new JsonArray();
            if (num == 1)
                for (int i = page * 200; i < Math.Min(Customers, page * 200 + 200); i++)
                    arr.Add(new JsonObject { ["ID"] = i + 1, ["TheName"] = $"عميل {i + 1}", ["AccountID"] = i + 1, ["Mobile"] = "", ["Address"] = null });
            else if (num == 11 && page == 0)
            {
                arr.Add(new JsonObject { ["AccountID"] = 1, ["Amount"] = -5000.5, ["CurrencyID"] = 1 });
                arr.Add(new JsonObject { ["AccountID"] = 2, ["Amount"] = 120000, ["CurrencyID"] = 1 });
            }
            return Json("{\"code\":200,\"ResultBody\":" + arr.ToJsonString() + "}");
        }
        if (path.EndsWith("GetDataListDistribution"))
            return Json("{\"code\":200,\"ResultBody\":[{\"ID\":1,\"CurrencyName\":\"ريال\"}]}");
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }
}

public class EbsCryptoTests
{
    [Theory]
    [InlineData("123456", "ghEUUleKg/2x0fhDZ97JwQ==\n")]
    [InlineData("كلمة", "MQzzHKLA6KJpHmRSbkONSQ==\n")]
    [InlineData("a", "vD18bVEUIPaTqM/KpiRMSA==\n")]
    public void Matches_reference_vectors_and_roundtrips(string plain, string expected)
    {
        Assert.Equal(expected, EbsCrypto.EncryptPassword(plain));
        Assert.Equal(plain, EbsCrypto.DecryptPassword(expected));
    }
}

public class EbsFlowTests
{
    private static readonly EbsSettings S = new()
    {
        UserName = "u", Password = "p", BranchName = "الفرع", BranchId = 1, UserId = 2, Currency = "ريال", DefaultPaymentType = "آجل",
        DefaultStoreName = "المخزن", WaterUnitName = "برميل", DefaultBarrelPrice = "2000", Year = 2026, DeviceUuid = "dev-1",
    };

    private const long Now = 1_800_000_000_000L;

    private static (EbsDb Db, EbsClient C, FakeEbs F) Make(bool withCustomers = true)
    {
        var f = new FakeEbs();
        var db = new EbsDb(":memory:");
        if (withCustomers) db.ReplaceCustomers(new[] { new EbsCustomer(1, "وايت أبو علي", null, null, 1), new EbsCustomer(2, "وايت 2", null, null, 2) });
        return (db, new EbsClient(S, new HttpClient(f)), f);
    }

    private static SwingRecord Swing(long id, string? truck, double total, long endMs = Now - 30 * 60_000L) =>
        new(id, "2026-11-13", 1, true, "1+2", endMs - 60_000, endMs, 25, 30, total, "", truck == null ? null : 1L, truck ?? "", "", 55.0);

    [Fact]
    public async Task Login_reports_each_failure_kind()
    {
        var (_, c, f) = Make();
        var ok = await c.LoginAsync(S);
        Assert.True(ok.Success); Assert.Equal("22795", ok.SessionId); Assert.Equal("موزع", ok.DistributorName);
        f.FailLogin = true;
        var bad = await new EbsClient(S, new HttpClient(f)).LoginAsync(S);
        Assert.False(bad.Success); Assert.Contains("EBS code 401", bad.ErrorMessage);
        f.ThrowNetwork = true;
        Assert.False((await new EbsClient(S, new HttpClient(f)).LoginAsync(S)).Success);
    }

    [Fact]
    public async Task Customer_download_pages_through_all_and_stores_balances()
    {
        var (db, c, f) = Make(withCustomers: false);
        var r = await new CustomerDownloadService(db, c).DownloadAsync(S);
        Assert.True(r.Success, r.Error);
        Assert.Equal(450, r.Count); Assert.Equal(450, db.CustomerCount());
        Assert.Equal(3 + 1, f.PageCalls);                                   // 3 صفحات عملاء + صفحة أرصدة
        Assert.Equal(2, r.BalanceCount);
        var bal = db.BalancesOf(1);
        Assert.Equal(("ريال", -5000.5), bal.Single());
        Assert.NotNull(db.FindCustomer("  عميل   7 "));
        var noIds = await new CustomerDownloadService(db, c).DownloadAsync(S with { UserId = 0 });
        Assert.False(noIds.Success); Assert.Contains("رقم المستخدم", noIds.Error);
    }

    [Fact]
    public void Enqueue_applies_every_guard_and_uses_the_approved_quantity_unchanged()
    {
        var (db, _, _) = Make();
        var q = new EbsInvoiceQueue(db);
        Assert.Equal(EnqueueStatus.NoTruck, q.Enqueue(Swing(1, null, 55), S, Now).Status);
        Assert.Equal(EnqueueStatus.ZeroQuantity, q.Enqueue(Swing(1, "وايت أبو علي", 0), S, Now).Status);
        Assert.Equal(EnqueueStatus.TooRecent, q.Enqueue(Swing(1, "وايت أبو علي", 55, Now - 5 * 60_000L), S, Now).Status);
        Assert.Equal(EnqueueStatus.UnknownCustomer, q.Enqueue(Swing(1, "غير موجود", 55), S, Now).Status);
        Assert.Equal(EnqueueStatus.NoPrice, q.Enqueue(Swing(1, "وايت أبو علي", 55), S with { DefaultBarrelPrice = "" }, Now).Status);
        var ok = q.Enqueue(Swing(1, "وايت أبو علي", 55), S, Now);
        Assert.Equal(EnqueueStatus.Queued, ok.Status);
        Assert.Equal(55.0, ok.Invoice!.Barrels);                              // الكمية المعتمدة كما أدخلها المسؤول، بلا خصم إضافي
        Assert.Equal(110_000.0, ok.Invoice.Total);                            // 55 × 2000
        Assert.Equal(EnqueueStatus.AlreadyQueued, q.Enqueue(Swing(1, "وايت أبو علي", 55), S, Now).Status);   // لا فاتورتان لزفة
        Assert.Single(db.Invoices(false));
    }

    [Fact]
    public async Task Sync_sends_master_then_details_once_with_the_same_number_and_is_idempotent()
    {
        var (db, c, f) = Make();
        var inv = new EbsInvoiceQueue(db).Enqueue(Swing(1, "وايت أبو علي", 55), S, Now).Invoice!;
        var svc = new InvoiceSyncService(db, c, () => S, nowMs: () => Now);
        var r = await svc.SyncOneAsync(inv.LocalId);
        Assert.True(r.Success, r.Error);
        var done = db.Invoice(inv.LocalId)!;
        Assert.Equal(InvoiceStatus.Synced, done.Status); Assert.Equal(101L, done.MasterId); Assert.Equal(501L, done.DetailsId);
        Assert.Equal(110_000.0, f.LastMaster!["الإجمالي"]!.GetValue<double>());
        Assert.Equal("وايت أبو علي", f.LastMaster["اسم الحساب"]!.GetValue<string>());
        Assert.Equal(55.0, f.LastDetails!["الكمية"]!.GetValue<double>());
        Assert.Equal(2000.0, f.LastDetails["سعر الوحدة"]!.GetValue<double>());
        Assert.Equal(101L, f.LastDetails["ParentID"]!.GetValue<long>());
        Assert.Equal(f.LastMaster["TheNumberFromApp"]!.ToString(), f.LastDetails["TheNumberFromApp"]!.ToString());
        Assert.Equal("2026-11-13".Length, f.LastMaster["التاريخ"]!.GetValue<string>().Length);
        await svc.SyncOneAsync(inv.LocalId);                                  // إعادة: لا إرسال
        await svc.SyncAllOrderedAsync();
        Assert.Equal(1, f.MasterCalls); Assert.Equal(1, f.DetailsCalls);
    }

    [Fact]
    public async Task Failed_details_never_recreates_the_master()
    {
        var (db, c, f) = Make();
        var inv = new EbsInvoiceQueue(db).Enqueue(Swing(1, "وايت أبو علي", 55), S, Now).Invoice!;
        var svc = new InvoiceSyncService(db, c, () => S, nowMs: () => Now);
        f.DetailsFails = true;
        var r1 = await svc.SyncOneAsync(inv.LocalId);
        Assert.False(r1.Success);
        var mid = db.Invoice(inv.LocalId)!;
        Assert.Equal(InvoiceStatus.SyncFailed, mid.Status); Assert.Equal(101L, mid.MasterId); Assert.Null(mid.DetailsId);
        f.DetailsFails = false;
        Assert.True((await svc.SyncOneAsync(inv.LocalId)).Success);
        Assert.Equal(1, f.MasterCalls); Assert.Equal(2, f.DetailsCalls);
    }

    [Fact]
    public async Task Duplicate_warning_waits_for_confirmation_and_then_marks_and_renumbers()
    {
        var (db, c, f) = Make();
        var q = new EbsInvoiceQueue(db);
        var a = q.Enqueue(Swing(1, "وايت أبو علي", 55, Now - 60 * 60_000L), S, Now).Invoice!;
        var b = q.Enqueue(Swing(2, "وايت 2", 40, Now - 30 * 60_000L), S, Now).Invoice!;
        var svc = new InvoiceSyncService(db, c, () => S, nowMs: () => Now);
        f.DuplicateOnce = true;
        var all = await svc.SyncAllOrderedAsync();
        Assert.Single(all); Assert.True(all[0].IsDuplicateWarning);          // توقفت عند الأولى ولم تتجاوزها
        Assert.Equal(InvoiceStatus.NeedsConfirm, db.Invoice(a.LocalId)!.Status);
        Assert.Equal(InvoiceStatus.LocalCreated, db.Invoice(b.LocalId)!.Status);
        var r = await svc.SyncOneAsync(a.LocalId, confirmDuplicate: true);
        Assert.True(r.Success, r.Error);
        Assert.Contains("Same_Record_Exist", f.LastMaster!["ملاحظات"]!.GetValue<string>());
        Assert.Equal(2, f.MasterNumbers.Count); Assert.NotEqual(f.MasterNumbers[0], f.MasterNumbers[1]);
        Assert.Single(await svc.SyncAllOrderedAsync());                       // الثانية تُرسَل الآن
        Assert.Equal(InvoiceStatus.Synced, db.Invoice(b.LocalId)!.Status);
    }

    [Fact]
    public async Task Network_failure_is_a_failed_result_not_an_exception_and_keeps_the_record()
    {
        var (db, c, f) = Make();
        var inv = new EbsInvoiceQueue(db).Enqueue(Swing(1, "وايت أبو علي", 55), S, Now).Invoice!;
        var svc = new InvoiceSyncService(db, c, () => S, nowMs: () => Now);
        await c.LoginAsync(S);
        f.ThrowNetwork = true;
        var r = await svc.SyncOneAsync(inv.LocalId);
        Assert.False(r.Success);
        var row = db.Invoice(inv.LocalId)!;
        Assert.Equal(InvoiceStatus.SyncFailed, row.Status); Assert.Equal(1, row.Attempts);
        Assert.Equal(inv.EbsNumericId, row.EbsNumericId);                    // الرقم لا يتغيّر بالإعادة العادية
    }
}
