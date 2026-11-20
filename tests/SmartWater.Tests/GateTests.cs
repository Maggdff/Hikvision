using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using SmartWater.Core;
using SmartWater.Gate;
using Xunit;
using static SmartWater.Tests.GateHarness;

namespace SmartWater.Tests;

public class GateAccessTests
{
    [Fact] // 1
    public async Task Valid_card_unlocks_the_box_and_tells_the_engine_which_truck_it_is()
    {
        using var h = new GateHarness();
        var r = await h.Tap("ESP1", "AA11");
        Assert.True(r.Allowed, r.Reason);
        Assert.Equal("unlock", r.Command!.Type);
        Assert.Equal(LockState.Unlocked, h.Db.GetDesired(1).State);
        Assert.Contains((1, T1), h.Stations.Expected);                      // الوايت من البطاقة لا من الصورة
        var op = Assert.Single(h.Db.ActiveOps());
        Assert.Equal(Name1, op.Customer);
        Assert.NotNull(h.Db.OpBoxes(op.Id).Single().UnlockedMs);            // أكّد الجهاز التنفيذ
        Assert.NotNull(h.Db.PermitById(r.PermitId!)!.UsedMs);              // التصريح استُعمل مرة واحدة
    }

    [Fact] // 2
    public async Task Unknown_and_revoked_cards_are_denied_and_logged()
    {
        using var h = new GateHarness();
        var r = await h.Tap("ESP1", "ZZ99");
        Assert.False(r.Allowed); Assert.Equal("unknown_card", r.Reason);
        Assert.True(h.Has("deny.unknown_card"));
        Assert.Empty(h.Db.ActiveOps());
        h.Svc.RevokeCard(h.Db.Cards().First().Id);
        Assert.Equal("card_revoked", (await h.Tap("ESP1", "AA11")).Reason);
        Assert.Equal(LockState.Locked, h.Db.GetDesired(1).State);           // لم يُفتح شيء
    }

    [Fact] // 3
    public async Task Balance_rule_prepaid_allows_enough_and_denies_not_enough()
    {
        using var h = new GateHarness();
        Assert.True((await h.Tap("ESP1", "AA11")).Allowed);                // 200000 مقابل 110000
        using var h2 = new GateHarness();
        h2.Balance.Result = new BalanceResult(true, 100_000, "live", 0);
        var d = await h2.Tap("ESP1", "AA11");
        Assert.Equal("insufficient_balance", d.Reason);
        using var h3 = new GateHarness();
        h3.Exposure.Pending = 100_000;                                      // فواتير غير مرسلة لا يراها EBS بعد
        Assert.Equal("insufficient_balance", (await h3.Tap("ESP1", "AA11")).Reason);
    }

    [Fact] // 5,6
    public async Task Required_amount_is_the_entered_quantity_times_price_with_no_extra_discount()
    {
        using var h = new GateHarness();
        h.Balance.Result = new BalanceResult(true, 110_000, "live", 0);     // بالضبط 55 × 2000
        Assert.True((await h.Tap("ESP1", "AA11")).Allowed);
        var op = h.Db.ActiveOps().Single();
        Assert.Equal(110_000.0, op.Required); Assert.Equal(2000.0, op.Price);
        using var h2 = new GateHarness();
        h2.Balance.Result = new BalanceResult(true, 109_999, "live", 0);
        Assert.Equal("insufficient_balance", (await h2.Tap("ESP1", "AA11")).Reason);
    }

    [Fact]
    public async Task Balance_unavailable_credit_limit_and_log_only_policies()
    {
        using var h = new GateHarness();
        h.Balance.Result = new BalanceResult(false, 0, "none", 0);
        Assert.Equal("balance_unavailable", (await h.Tap("ESP1", "AA11")).Reason);
        using var lim = new GateHarness(new GateOptions { RestartGraceMin = 0, Policy = BalancePolicy.CreditLimit, CreditLimit = 20_000 });
        lim.Balance.Result = new BalanceResult(true, 95_000, "live", 0);    // 95000 − 110000 = −15000 ≥ −20000
        Assert.True((await lim.Tap("ESP1", "AA11")).Allowed);
        using var lim2 = new GateHarness(new GateOptions { RestartGraceMin = 0, Policy = BalancePolicy.CreditLimit, CreditLimit = 10_000 });
        lim2.Balance.Result = new BalanceResult(true, 95_000, "live", 0);
        Assert.Equal("insufficient_balance", (await lim2.Tap("ESP1", "AA11")).Reason);
        using var log = new GateHarness(new GateOptions { RestartGraceMin = 0, Policy = BalancePolicy.LogOnly });
        log.Balance.Result = new BalanceResult(false, 0, "none", 0);
        Assert.True((await log.Tap("ESP1", "AA11")).Allowed);
    }

    [Fact]
    public async Task Each_customer_has_their_own_credit_ceiling_and_others_use_the_default()
    {
        GateHarness P2(double defaultLimit) => new(new GateOptions { RestartGraceMin = 0, Policy = BalancePolicy.CreditLimit, CreditLimit = defaultLimit });

        using var a = P2(10_000);                                                    // وايت 1 يحتاج 110000
        a.Db.SetCustomerLimit("  وايت   أبو علي ", 30_000);                           // يُطبَّع الاسم (مسافات)
        a.Balance.Result = new BalanceResult(true, 80_000, "live", 0);               // العجز 30000 = سقفه بالضبط
        Assert.True((await a.Tap("ESP1", "AA11")).Allowed);

        using var b = P2(10_000);
        b.Db.SetCustomerLimit(Name1, 29_999);
        b.Balance.Result = new BalanceResult(true, 80_000, "live", 0);               // العجز 30000 > 29999
        Assert.Equal("insufficient_balance", (await b.Tap("ESP1", "AA11")).Reason);

        using var c = P2(10_000);                                                    // وايت 2 بلا سقف خاص: الافتراضي 10000، ويحتاج 80000
        c.Balance.Result = new BalanceResult(true, 75_000, "live", 0);               // عجز 5000
        Assert.True((await c.Tap("ESP3", "BB22")).Allowed);
        using var d = P2(10_000);
        d.Balance.Result = new BalanceResult(true, 60_000, "live", 0);               // عجز 20000 > 10000
        Assert.Equal("insufficient_balance", (await d.Tap("ESP3", "BB22")).Reason);

        using var e = P2(10_000);                                                    // سقف خاص صفر = لا دين، رغم الافتراضي
        e.Db.SetCustomerLimit(Name2, 0);
        e.Balance.Result = new BalanceResult(true, 79_999, "live", 0);
        Assert.Equal("insufficient_balance", (await e.Tap("ESP3", "BB22")).Reason);
        e.Balance.Result = new BalanceResult(true, 80_000, "live", 0);
        Assert.True((await e.Tap("ESP3", "BB22")).Allowed);

        Assert.Equal(30_000.0, a.Db.CustomerLimit(Name1));
        a.Svc.RemoveCustomerLimit(Name1);
        Assert.Null(a.Db.CustomerLimit(Name1));
        Assert.Contains(a.Db.Events(50, "admin.limit_removed"), x => x.Data == Name1);
    }

    [Fact]
    public async Task System_not_ready_license_and_customer_checks_deny()
    {
        using var h = new GateHarness();
        h.Set(camera: false);
        Assert.Equal("engine_offline", (await h.Tap("ESP1", "AA11")).Reason);
        h.Set(frameAge: 60);
        Assert.Equal("engine_offline", (await h.Tap("ESP1", "AA11")).Reason);
        h.Idle(1); h.License.Ok = false;
        Assert.Equal("license_blocked", (await h.Tap("ESP1", "AA11")).Reason);
        h.License.Ok = true; h.Trucks.Customers.Remove(Name1);
        Assert.Equal("customer_not_found", (await h.Tap("ESP1", "AA11")).Reason);
        h.Trucks.Customers.Add(Name1); h.Price.Price = null;
        Assert.Equal("price_missing", (await h.Tap("ESP1", "AA11")).Reason);
        Assert.Empty(h.Db.ActiveOps());
    }

    [Fact]
    public async Task Learning_mode_captures_the_next_card_hash_and_registers_it_without_opening_anything()
    {
        using var h = new GateHarness();
        h.Svc.StartLearning(1);
        await h.Tap("ESP2", "NEW1", ack: false);                                         // صندوق آخر: لا يلتقط
        Assert.True(h.Svc.LearningState().Active);
        var r = await h.Tap("ESP1", "NEW1", ack: false);
        Assert.False(r.Allowed); Assert.Equal("learning", r.Reason);
        var (active, hash) = h.Svc.LearningState();
        Assert.False(active); Assert.NotNull(hash);
        Assert.Empty(h.Db.ActiveOps());
        h.Svc.RegisterCardByHash(hash!, "جديدة", "سائق", T2);
        Assert.True((await h.Tap("ESP3", "NEW1")).Allowed);                              // صارت بطاقة صالحة
        Assert.Throws<InvalidOperationException>(() => h.Svc.RegisterCardByHash(hash!, "x", "y", T1));
        h.Svc.StartLearning(1, 5); h.Clock.Advance(10);
        Assert.False(h.Svc.LearningState().Active);                                      // انتهت المدة
    }

    [Fact]
    public async Task Repeated_denied_taps_are_rate_limited()
    {
        using var h = new GateHarness();
        for (int i = 0; i < 5; i++) await h.Tap("ESP1", "ZZ99");
        Assert.Equal("too_many_attempts", (await h.Tap("ESP1", "ZZ99")).Reason);
        h.Clock.Advance(61);
        Assert.Equal("unknown_card", (await h.Tap("ESP1", "ZZ99")).Reason);
    }
}

public class GateStationTests
{
    [Fact] // 7, 8
    public async Task Single_meter_operations_use_only_their_own_box()
    {
        using var h = new GateHarness();
        await h.Tap("ESP1", "AA11");
        Assert.Single(h.Db.OpBoxes(h.Db.ActiveOps().Single().Id));
        using var h2 = new GateHarness();
        await h2.Tap("ESP2", "AA11");                                       // الحنفية الثانية فقط
        var ob = Assert.Single(h2.Db.OpBoxes(h2.Db.ActiveOps().Single().Id));
        Assert.Equal(2, ob.BoxId);
    }

    [Fact] // 9, 10, 11 (جمع الكميتين وزفة واحدة يتحققان في FillSession؛ هنا عملية واحدة بصندوقين)
    public async Task Both_boxes_of_the_same_truck_share_one_operation_and_one_balance_check()
    {
        using var h = new GateHarness();
        await h.Tap("ESP1", "AA11"); await h.Tap("ESP2", "AA11");
        var op = Assert.Single(h.Db.ActiveOps());
        Assert.Equal(2, h.Db.OpBoxes(op.Id).Count);
        Assert.Equal(1, h.Balance.Calls);                                   // لا فحص/خصم ثانٍ للانضمام
        Assert.Equal(LockState.Unlocked, h.Db.GetDesired(2).State);
    }

    [Fact] // 24, 25
    public async Task Another_truck_cannot_use_a_busy_station_and_a_customer_cannot_run_two_stations()
    {
        using var h = new GateHarness();
        await h.Tap("ESP1", "AA11");
        Assert.Equal("station_busy", (await h.Tap("ESP2", "BB22")).Reason);   // وايت آخر على نفس المحطة
        Assert.Equal("truck_busy", (await h.Tap("ESP3", "AA11")).Reason);     // نفس العميل على محطة أخرى
        Assert.True((await h.Tap("ESP3", "BB22")).Allowed);                   // وايت آخر على محطة حرة
        Assert.Equal(2, h.Db.ActiveOps().Count);
    }

    [Fact]
    public async Task Retapping_the_same_box_never_creates_a_second_operation()
    {
        using var h = new GateHarness();
        await h.Tap("ESP1", "AA11"); await h.Tap("ESP1", "AA11"); await h.Tap("ESP1", "AA11");
        Assert.Single(h.Db.ActiveOps());                                    // 15: لا عملية/زفة مكررة
        Assert.Equal(1, h.Balance.Calls);
    }

    [Fact] // 12, 13, 16
    public async Task Box_is_never_locked_while_any_participating_meter_runs_or_the_grace_is_pending()
    {
        using var h = new GateHarness();
        await h.Tap("ESP1", "AA11"); await h.Tap("ESP2", "AA11");
        h.Set(m0: true, m1: true, state: FillState.Counting, session: "S1", flowed: true);
        h.Run(5, "ESP1", "ESP2");
        Assert.Equal(OpState.Filling, h.Db.ActiveOps().Single().State);
        h.Set(m0: false, m1: true, state: FillState.Counting, session: "S1", flowed: true);   // الأول توقف والثاني يعمل
        h.Run(30, "ESP1", "ESP2");
        Assert.Empty(h.LockCommands(1)); Assert.Empty(h.LockCommands(2));
        h.Set(m0: false, m1: false, state: FillState.StopWait, session: "S1", flowed: true);  // مهلة الدقيقة جارية
        h.Run(30, "ESP1", "ESP2");
        Assert.Empty(h.LockCommands(1)); Assert.Empty(h.LockCommands(2));
        Assert.Equal(LockState.Unlocked, h.Db.GetDesired(1).State);
        h.Set(state: FillState.Paused, session: "S1", flowed: true);                          // انتهت المهلة
        h.Run(5, "ESP1", "ESP2");
        Assert.Equal(LockState.Locked, h.Db.GetDesired(1).State); Assert.Equal(LockState.Locked, h.Db.GetDesired(2).State);
        Assert.Equal(OpState.Closed, h.Db.Op(h.Db.Operations().First().Id)!.State);           // أكّد الجهازان القفل
    }

    [Fact] // 17
    public async Task Unknown_state_blocks_locking_until_it_is_known_again()
    {
        using var h = new GateHarness();
        await h.Tap("ESP1", "AA11");
        h.Set(m0: true, state: FillState.Counting, session: "S1", flowed: true); h.Run(3, "ESP1");
        h.Set(state: FillState.Paused, session: "S1", flowed: true, age: 30);                 // لا قراءة سليمة منذ 30 ث
        h.Run(20, "ESP1"); Assert.Empty(h.LockCommands(1));
        h.Set(state: FillState.Paused, session: "S1", flowed: true, camera: false);           // الكاميرا منقطعة
        h.Run(20, "ESP1"); Assert.Empty(h.LockCommands(1));
        h.Set(state: FillState.Paused, session: "S1", flowed: true, engine: false);           // المحرك متوقف
        h.Run(20, "ESP1"); Assert.Empty(h.LockCommands(1));
        h.Set(state: FillState.Paused, session: "S1", flowed: true);                          // معروفة وساكنة
        h.Run(3, "ESP1"); Assert.NotEmpty(h.LockCommands(1));
    }

    [Fact]
    public async Task Flow_resuming_before_the_lock_is_executed_cancels_it()
    {
        using var h = new GateHarness();
        await h.Tap("ESP1", "AA11");
        h.Set(m0: true, state: FillState.Counting, session: "S1", flowed: true); h.Run(3);
        h.Set(state: FillState.Paused, session: "S1", flowed: true); h.Run(2);                // أُصدر القفل لكن لم يصل (لا نبضات)
        Assert.Equal(OpState.Locking, h.Db.ActiveOps().Single().State);
        h.Set(m0: true, state: FillState.Counting, session: "S1", flowed: true); h.Run(2);
        Assert.Equal(OpState.Filling, h.Db.ActiveOps().Single().State);
        Assert.Equal(LockState.Unlocked, h.Db.GetDesired(1).State);
        Assert.True(h.Has("op.lock_cancelled"));
    }

    [Fact] // 14, 15
    public async Task Same_truck_resuming_the_same_session_reuses_it_without_a_second_charge()
    {
        using var h = new GateHarness();
        await h.Tap("ESP1", "AA11");
        h.Set(m0: true, state: FillState.Counting, session: "S1", flowed: true); h.Run(3, "ESP1");
        h.Set(state: FillState.Paused, session: "S1", flowed: true); h.Run(5, "ESP1");
        Assert.Equal(OpState.Closed, h.Db.Operations().First().State);
        h.Balance.Result = new BalanceResult(true, 10, "live", 0);                             // الرصيد لا يكفي لعملية جديدة...
        var r = await h.Tap("ESP1", "AA11");                                                   // ...لكنها استئناف لنفس الجلسة
        Assert.True(r.Allowed, r.Reason);
        Assert.Equal(1, h.Balance.Calls);                                                      // لم يُفحص الرصيد ثانية
        Assert.True(h.Db.ActiveOps().Single().Resumed);
        Assert.Equal("S1", h.Db.ActiveOps().Single().SessionId);
    }

    [Fact]
    public async Task Unused_permit_is_locked_after_ten_minutes_but_not_before()
    {
        using var h = new GateHarness();
        await h.Tap("ESP1", "AA11");
        h.Run(500, "ESP1"); Assert.Empty(h.LockCommands(1));
        h.Run(150, "ESP1");
        Assert.NotEmpty(h.LockCommands(1));
        Assert.Equal(OpState.Closed, h.Db.Operations().First().State);
    }

    [Fact] // 21 (جزئي): إعادة تشغيل الجسر
    public async Task Bridge_restart_keeps_operations_and_blocks_locking_during_the_grace_window()
    {
        using var h = new GateHarness(new GateOptions { RestartGraceMin = 15, CardSecret = new byte[32] });
        h.Clock.Advance(16 * 60);                                         // انتهت مهلة الإقلاع الأولى
        await h.Tap("ESP1", "AA11");
        h.Set(m0: true, state: FillState.Counting, session: "S1", flowed: true); h.Run(3, "ESP1");
        h.RestartBridge();                                                 // إعادة تشغيل: العملية محفوظة
        Assert.Single(h.Db.ActiveOps());
        h.Set(state: FillState.Paused, session: "S1", flowed: true);
        h.Run(600, "ESP1");                                                // 10 دقائق < 15: لا قفل
        Assert.Empty(h.LockCommands(1));
        h.Run(400, "ESP1");
        Assert.NotEmpty(h.LockCommands(1));
    }

    [Fact]
    public async Task Unconfirmed_lock_is_retried_then_reported_as_a_fault()
    {
        using var h = new GateHarness();
        await h.Tap("ESP1", "AA11");
        h.Set(m0: true, state: FillState.Counting, session: "S1", flowed: true); h.Run(3, "ESP1");
        h.Set(state: FillState.Paused, session: "S1", flowed: true);
        h.Run(400);                                                        // الجهاز لا يرد على الإطلاق
        Assert.Equal(3, h.LockCommands(1).Max(c => c.Attempts));
        Assert.True(h.Has("fault.lock_unconfirmed"));
        Assert.Equal(OpState.Locking, h.Db.ActiveOps().Single().State);   // لا إغلاق وهمي
    }

    [Fact]
    public async Task Flow_without_a_permit_is_reported_but_never_blocked()
    {
        using var h = new GateHarness();
        h.Set(m1: true, state: FillState.Counting, session: "X", flowed: true);
        h.Run(3);
        Assert.True(h.Has("fault.unauthorized_flow"));
        Assert.Empty(h.Db.ActiveOps());
        await Task.CompletedTask;
    }
}

public class GateDeviceTests
{
    [Fact] // 22
    public async Task Expired_commands_are_not_delivered_and_a_late_ack_is_flagged()
    {
        using var h = new GateHarness();
        var r = await h.Tap("ESP1", "AA11", ack: false);
        h.Clock.Advance(30);                                                // انتهت صلاحية أمر الفتح (15 ث) وتجاوزت مهلة التأكيد المتأخر (10 ث)
        var hb = h.Hb("ESP1", LockState.Locked);
        Assert.Empty(hb.Commands);
        var ack = h.Svc.Ack("ESP1", r.Command!.CmdId, true, LockState.Unlocked);
        Assert.True(h.Has("fault.late_ack"));
        Assert.True(ack.Accepted);
    }

    [Fact]
    public async Task Heartbeat_renews_the_lease_only_while_an_operation_is_active_and_offline_devices_are_detected()
    {
        using var h = new GateHarness();
        await h.Tap("ESP1", "AA11");
        long l1 = h.Db.GetDesired(1).LeaseUntilMs;
        h.Clock.Advance(30);
        h.Hb("ESP1", LockState.Unlocked);
        Assert.True(h.Db.GetDesired(1).LeaseUntilMs > l1);                  // جُدِّد
        h.Run(60);                                                         // لا نبضات من ESP1 → غير متصل
        Assert.True(h.Has("device.offline"));
        h.Hb("ESP1", LockState.Unlocked);
        Assert.True(h.Has("device.online"));
    }

    [Fact]
    public void Offline_events_are_deduplicated_by_device_and_sequence()
    {
        using var h = new GateHarness();
        var evs = new[] { (1L, 100L, "tap", "x"), (2L, 101L, "reader_error", "y") };
        Assert.Equal(2, h.Svc.UploadEvents("ESP1", evs));
        Assert.Equal(0, h.Svc.UploadEvents("ESP1", evs));
        Assert.True(h.Has("dev.tap"));
    }

    [Fact]
    public async Task Failed_ack_and_manual_unlock_are_audited()
    {
        using var h = new GateHarness();
        var r = await h.Tap("ESP1", "AA11", ack: false);
        h.Svc.Ack("ESP1", r.Command!.CmdId, false, LockState.Locked);
        Assert.True(h.Has("fault.command_failed"));
        h.Svc.ManualUnlock(3, "عطل القارئ", "المسؤول");
        Assert.Contains(h.Db.Events(50, "admin.manual_unlock"), e => e.Data.Contains("عطل القارئ"));
        Assert.Equal(LockState.Unlocked, h.Db.GetDesired(3).State);
    }

    // ---------------- المصادقة (23)
    private static (string Sig, long Ts) Sign(GateHarness h, string dev, long seq, string nonce, byte[] body, long? ts = null)
    {
        long t = ts ?? h.Clock.NowMs;
        return (GateAuth.Sign(h.Keys[dev], "POST", "/api/access/check", dev, seq, t, nonce, body), t);
    }

    [Fact]
    public void Authentication_rejects_replays_stale_time_bad_signatures_and_unknown_devices()
    {
        using var h = new GateHarness();
        var body = Encoding.UTF8.GetBytes("{\"uid\":\"AA11\"}");
        var (sig, ts) = Sign(h, "ESP1", 10, "n1", body);
        Assert.Equal(AuthStatus.Ok, h.Svc.Authenticate("ESP1", 10, ts, "n1", sig, "POST", "/api/access/check", body));
        Assert.Equal(AuthStatus.Replay, h.Svc.Authenticate("ESP1", 10, ts, "n1", sig, "POST", "/api/access/check", body));      // نفس الطلب
        var (sig2, ts2) = Sign(h, "ESP1", 9, "n2", body);
        Assert.Equal(AuthStatus.Replay, h.Svc.Authenticate("ESP1", 9, ts2, "n2", sig2, "POST", "/api/access/check", body));      // تسلسل أقدم
        var (sig3, ts3) = Sign(h, "ESP1", 11, "n1", body);
        Assert.Equal(AuthStatus.Replay, h.Svc.Authenticate("ESP1", 11, ts3, "n1", sig3, "POST", "/api/access/check", body));     // nonce مكرر
        var (sig4, ts4) = Sign(h, "ESP1", 12, "n4", body, h.Clock.NowMs - 120_000);
        Assert.Equal(AuthStatus.StaleTime, h.Svc.Authenticate("ESP1", 12, ts4, "n4", sig4, "POST", "/api/access/check", body)); // وقت قديم
        var (sig5, ts5) = Sign(h, "ESP1", 13, "n5", body);
        Assert.Equal(AuthStatus.BadSignature, h.Svc.Authenticate("ESP1", 13, ts5, "n5", sig5.Replace(sig5[0], sig5[0] == 'a' ? 'b' : 'a'), "POST", "/api/access/check", body));
        Assert.Equal(AuthStatus.BadSignature, h.Svc.Authenticate("ESP1", 13, ts5, "n5", sig5, "POST", "/api/access/check", Encoding.UTF8.GetBytes("{\"uid\":\"BB22\"}")));   // جسم مُعدَّل
        Assert.Equal(AuthStatus.UnknownDevice, h.Svc.Authenticate("NOPE", 1, ts5, "n", "00", "POST", "/api/access/check", body));
        var (sig6, ts6) = Sign(h, "ESP2", 1, "x", body);                       // توقيع جهاز آخر لا يصلح لهذا الجهاز
        Assert.Equal(AuthStatus.BadSignature, h.Svc.Authenticate("ESP1", 14, ts6, "x", sig6, "POST", "/api/access/check", body));
    }

    [Fact]
    public void Signing_matches_the_reference_vector_used_by_the_firmware()
    {
        // نفس المتجه في firmware/tools/sign_check.py وفي أمر SELFTEST على الجهاز
        var key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var body = Encoding.UTF8.GetBytes("{\"uid\":\"04A1B2C3\"}");
        Assert.Equal("POST\n/api/access/check\nESP1\n7\n1800000000123\n0123456789abcdef\n5f97c4cade46a304d020327ebc47523d388f7d4db05f7e3243be69b06281fc5a",
            GateAuth.Canonical("POST", "/api/access/check", "ESP1", 7, 1800000000123, "0123456789abcdef", body));
        Assert.Equal("698a6bfb075693abd6a7e1553ef3dba3bbee17b293023dbf3e89584c633ae08f",
            GateAuth.Sign(key, "POST", "/api/access/check", "ESP1", 7, 1800000000123, "0123456789abcdef", body));
    }

    // ---------------- بروتوكول HTTP كاملًا على Kestrel حقيقي
    [Fact]
    public async Task Http_roundtrip_heartbeat_check_ack_with_signed_requests()
    {
        using var h = new GateHarness();
        int port; var l = new TcpListener(IPAddress.Loopback, 0); l.Start(); port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop();
        await using var app = await GateHost.StartAsync(h.Svc, $"http://127.0.0.1:{port}");
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        long seq = 100;

        async Task<(HttpStatusCode, JsonElement)> Post(string dev, string path, string json, bool sign = true)
        {
            var body = Encoding.UTF8.GetBytes(json);
            long s = ++seq; string nonce = Guid.NewGuid().ToString("N"); long ts = h.Clock.NowMs;
            var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = new ByteArrayContent(body) };
            req.Headers.Add("X-Device", dev); req.Headers.Add("X-Seq", s.ToString()); req.Headers.Add("X-Ts", ts.ToString()); req.Headers.Add("X-Nonce", nonce);
            req.Headers.Add("X-Sig", sign ? GateAuth.Sign(h.Keys[dev], "POST", path, dev, s, ts, nonce, body) : "00");
            var resp = await http.SendAsync(req);
            var text = await resp.Content.ReadAsStringAsync();
            return (resp.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
        }

        var (c0, _) = await Post("ESP1", "/api/device/heartbeat", "{\"lockState\":\"locked\",\"readerOk\":true,\"fw\":\"t\"}", sign: false);
        Assert.Equal(HttpStatusCode.Unauthorized, c0);

        var (c1, hb) = await Post("ESP1", "/api/device/heartbeat", "{\"lockState\":\"locked\",\"readerOk\":true,\"fw\":\"t\"}");
        Assert.Equal(HttpStatusCode.OK, c1);
        Assert.Equal("Locked", hb.GetProperty("desired").GetProperty("state").GetString());

        var (c2, chk) = await Post("ESP1", "/api/access/check", "{\"uid\":\"AA11\"}");
        Assert.Equal(HttpStatusCode.OK, c2);
        Assert.True(chk.GetProperty("allowed").GetBoolean());
        string cmd = chk.GetProperty("command").GetProperty("cmdId").GetString()!;

        var (c3, ack) = await Post("ESP1", "/api/access/ack", $"{{\"cmdId\":\"{cmd}\",\"executed\":true,\"lockState\":\"unlocked\"}}");
        Assert.True(ack.GetProperty("accepted").GetBoolean());
        Assert.Equal(LockState.Unlocked, h.Db.Device("ESP1")!.Actual);

        var (_, deny) = await Post("ESP2", "/api/access/check", "{\"uid\":\"BB22\"}");
        Assert.False(deny.GetProperty("allowed").GetBoolean());
        Assert.Equal("المحطة مشغولة بوايت آخر", deny.GetProperty("reasonAr").GetString());
    }
}
