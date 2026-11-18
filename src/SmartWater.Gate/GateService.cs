using System.Security.Cryptography;
using System.Text;
using SmartWater.Core;

namespace SmartWater.Gate;

public sealed record GateDeps(
    IStationView Stations, IStationControl Control, ITruckDirectory Trucks, IBalanceProvider Balance,
    IExposureProvider Exposure, IPriceProvider Price, ILicenseGate License, IClock Clock, IKeyProtector Protector);

/// <summary>
/// WaterGateBridge: القرار والتنسيق. لا يخصم ولا يملك رصيدًا ولا يسجّل زفات؛ يقرأ حالة المحرك والرصيد ويصدر تصاريح وأوامر قفل/فتح.
/// القفل لا يُرسل إلا إذا انتهت التعبئة وكل العدّادات المشاركة ساكنة وحالتها معروفة (قراءة سليمة حديثة، كاميرا ومحرك شغّالان).
/// </summary>
public sealed class GateService
{
    private static readonly Dictionary<string, string> ReasonAr = new()
    {
        ["ok"] = "تم السماح", ["unknown_device"] = "جهاز غير معروف", ["unknown_card"] = "بطاقة غير معروفة", ["card_revoked"] = "بطاقة ملغاة",
        ["box_disabled"] = "الصندوق معطّل", ["station_busy"] = "المحطة مشغولة بوايت آخر", ["truck_busy"] = "للعميل عملية جارية في محطة أخرى",
        ["insufficient_balance"] = "الرصيد غير كافٍ", ["balance_unavailable"] = "تعذّر التحقق من الرصيد", ["customer_not_found"] = "العميل غير موجود في EBS",
        ["truck_not_found"] = "الوايت غير موجود", ["license_blocked"] = "الترخيص موقوف", ["engine_offline"] = "النظام غير جاهز (الكاميرا/المحرك)",
        ["price_missing"] = "سعر البرميل غير محدد", ["too_many_attempts"] = "محاولات كثيرة، انتظر دقيقة",
    };

    /// <summary>نص عربي لسبب الرفض (للوحة التحكم).</summary>
    public static string ReasonArabic(string reason) => ReasonAr.GetValueOrDefault(reason, reason);

    private (int BoxId, long Until)? _learn;
    private string? _learnedHash;

    private readonly GateDb _db;
    private readonly GateOptions _o;
    private readonly GateDeps _d;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _checkGate = new(1, 1);
    private readonly long _startedMs;
    private readonly Dictionary<string, long> _nonces = new();
    private readonly Dictionary<string, Queue<long>> _denied = new();
    private readonly Dictionary<string, long> _cool = new();

    public GateService(GateDb db, GateOptions options, GateDeps deps)
    {
        _db = db; _o = options; _d = deps;
        _startedMs = deps.Clock.NowMs;
    }

    private long Now => _d.Clock.NowMs;
    private static string NewId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private void Log(string kind, int? boxId = null, string? deviceId = null, long? opId = null, long? cardId = null, string data = "") =>
        _db.Log(Now, kind, boxId, deviceId, opId, cardId, data);

    private void LogCool(string key, long cooldownMs, string kind, int? boxId = null, string? deviceId = null, long? opId = null, string data = "")
    {
        long now = Now;
        if (_cool.TryGetValue(key, out var until) && now < until) return;
        _cool[key] = now + cooldownMs;
        _db.Log(now, kind, boxId, deviceId, opId, null, data);
    }

    // ------------------------------------------------------------ الإدارة
    public string HashUid(string uid)
    {
        using var h = new HMACSHA256(_o.CardSecret);
        return Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes(uid.Trim().ToUpperInvariant()))).ToLowerInvariant();
    }

    /// <summary>يسجّل جهازًا بمفتاح عشوائي جديد ويعيده خامًا مرة واحدة ليُحقن في الجهاز؛ المخزَّن مشفّر.</summary>
    public byte[] AddDevice(string deviceId)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        _db.RegisterDevice(deviceId, _d.Protector.Protect(key));
        Log("admin.device_added", deviceId: deviceId);
        return key;
    }

    public Card RegisterCard(string uid, string label, string driver, long truckId)
    {
        var c = _db.AddCard(HashUid(uid), label, driver, truckId, Now);
        Log("admin.card_added", cardId: c.Id, data: label);
        return c;
    }

    /// <summary>تسجيل بطاقة ببصمتها (من وضع التعلّم). يرمي InvalidOperationException إن كانت مسجَّلة.</summary>
    public Card RegisterCardByHash(string uidHash, string label, string driver, long truckId)
    {
        if (_db.CardByHash(uidHash) != null) throw new InvalidOperationException("هذه البطاقة مسجَّلة مسبقًا");
        var c = _db.AddCard(uidHash, label, driver, truckId, Now);
        lock (_lock) { if (_learnedHash == uidHash) _learnedHash = null; }
        Log("admin.card_added", cardId: c.Id, data: label);
        return c;
    }

    /// <summary>وضع التعلّم: أول بطاقة تُمرَّر على هذا الصندوق خلال المدة لا تُعامَل كطلب دخول بل تُلتقط بصمتها للتسجيل.</summary>
    public void StartLearning(int boxId, int seconds = 60)
    {
        lock (_lock) { _learn = (boxId, Now + seconds * 1000L); _learnedHash = null; }
    }

    public void CancelLearning() { lock (_lock) { _learn = null; _learnedHash = null; } }

    /// <summary>Active = ما زال ينتظر بطاقة. CapturedHash = بصمة البطاقة الملتقطة (إن التُقطت).</summary>
    public (bool Active, string? CapturedHash) LearningState()
    {
        lock (_lock)
        {
            if (_learn is { } l && Now >= l.Until) _learn = null;
            return (_learn != null, _learnedHash);
        }
    }

    public void RevokeCard(long cardId) { _db.RevokeCard(cardId, Now); Log("admin.card_revoked", cardId: cardId); }

    /// <summary>فتح يدوي إداري (من نافذة ويندوز فقط): يُسجَّل السبب والمسؤول. بلا عملية ولا خصم ولا فاتورة.</summary>
    public string ManualUnlock(int boxId, string reason, string admin)
    {
        lock (_lock)
        {
            var box = _db.BoxById(boxId) ?? throw new ArgumentException("صندوق غير موجود");
            long now = Now;
            var des = _db.SetDesired(boxId, LockState.Unlocked, now + _o.UnlockLeaseSec * 1000);
            var cmdId = NewId();
            _db.AddCommand(new Command(cmdId, box.DeviceId, boxId, CommandType.Unlock, des.Version, now, now + _o.UnlockCmdTtlSec * 1000, CommandStatus.Pending, 0, null, 0));
            Log("admin.manual_unlock", boxId, box.DeviceId, data: $"{admin}: {reason}");
            return cmdId;
        }
    }

    public void ManualLock(int boxId, string reason, string admin)
    {
        lock (_lock)
        {
            var box = _db.BoxById(boxId) ?? throw new ArgumentException("صندوق غير موجود");
            long now = Now;
            var des = _db.SetDesired(boxId, LockState.Locked, 0);
            _db.AddCommand(new Command(NewId(), box.DeviceId, boxId, CommandType.Lock, des.Version, now, now + _o.LockCmdTtlSec * 1000, CommandStatus.Pending, 0, null, 0));
            Log("admin.manual_lock", boxId, box.DeviceId, data: $"{admin}: {reason}");
        }
    }

    // ------------------------------------------------------------ المصادقة
    public AuthStatus Authenticate(string deviceId, long seq, long ts, string nonce, string sig, string method, string path, byte[] body)
    {
        long now = Now;
        var prot = _db.DeviceKey(deviceId);
        if (prot == null) { LogCool("au:" + deviceId, 60_000, "auth.unknown_device", deviceId: deviceId); return AuthStatus.UnknownDevice; }
        var key = _d.Protector.Unprotect(prot);
        if (!GateAuth.SigEquals(GateAuth.Sign(key, method, path, deviceId, seq, ts, nonce, body), sig ?? ""))
        { LogCool("ab:" + deviceId, 60_000, "auth.bad_signature", deviceId: deviceId); return AuthStatus.BadSignature; }
        if (Math.Abs(now - ts) > _o.AuthSkewSec * 1000) { LogCool("as:" + deviceId, 60_000, "auth.stale_time", deviceId: deviceId); return AuthStatus.StaleTime; }
        lock (_lock)
        {
            var dev = _db.Device(deviceId);
            if (dev == null || seq <= dev.LastSeq) { LogCool("ar:" + deviceId, 60_000, "auth.replay", deviceId: deviceId, data: $"seq={seq}"); return AuthStatus.Replay; }
            foreach (var k in _nonces.Where(kv => kv.Value < now).Select(kv => kv.Key).ToList()) _nonces.Remove(k);
            string nk = deviceId + ":" + nonce;
            if (_nonces.ContainsKey(nk)) { LogCool("an:" + deviceId, 60_000, "auth.replay", deviceId: deviceId, data: "nonce"); return AuthStatus.Replay; }
            _nonces[nk] = now + 5 * 60_000L;
            _db.SetDeviceSeq(deviceId, seq);
        }
        return AuthStatus.Ok;
    }

    // ------------------------------------------------------------ تمرير بطاقة
    private bool EngineReady(StationSnapshot s) => s.EngineRunning && s.CameraConnected && s.FrameAgeSec <= _o.FrameMaxAgeSec;

    private bool Limited(string hash, long now)
    {
        if (!_denied.TryGetValue(hash, out var q)) return false;
        while (q.Count > 0 && now - q.Peek() > 60_000) q.Dequeue();
        return q.Count >= _o.MaxDeniedPerMin;
    }

    private void RecordDenied(string? hash, long now)
    {
        if (hash == null) return;
        if (!_denied.TryGetValue(hash, out var q)) _denied[hash] = q = new Queue<long>();
        q.Enqueue(now);
    }

    private CheckResponse Deny(string reason, Box? box, string deviceId, Card? card, string? uidHash, string data = "")
    {
        long now = Now;
        if (reason is "unknown_card" or "card_revoked" or "insufficient_balance" or "balance_unavailable") RecordDenied(uidHash, now);
        Log("deny." + reason, box?.Id, deviceId, null, card?.Id, data);
        return new CheckResponse(false, reason, ReasonAr.GetValueOrDefault(reason, reason), null, null, now, "red");
    }

    private static CommandDto Dto(Command c) => new(c.Id, c.Type.ToString().ToLowerInvariant(), c.Version, c.ExpiresMs);

    public async Task<CheckResponse> CheckAsync(string deviceId, string uid, CancellationToken ct = default)
    {
        await _checkGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            string hash = HashUid(uid);
            Box box; Card card; TruckInfo truck; StationSnapshot snap; Operation? op; bool resume = false;
            lock (_lock)
            {
                long now = Now;
                var b = _db.BoxOfDevice(deviceId);
                if (b == null) return Deny("unknown_device", null, deviceId, null, hash);
                box = b;
                if (Limited(hash, now)) return Deny("too_many_attempts", box, deviceId, null, hash);
                if (_learn is { } lr && lr.BoxId == box.Id && now < lr.Until)
                {
                    _learnedHash = hash; _learn = null;                     // التقاط للتسجيل: لا دخول ولا فتح
                    Log("admin.card_learned", box.Id, deviceId);
                    return new CheckResponse(false, "learning", "تم التقاط البطاقة للتسجيل", null, null, now, "blue");
                }
                var c = _db.CardByHash(hash);
                if (c == null) return Deny("unknown_card", box, deviceId, null, hash);
                card = c;
                if (card.Status != CardStatus.Active) return Deny("card_revoked", box, deviceId, card, hash);
                if (!box.Enabled) return Deny("box_disabled", box, deviceId, card, hash);
                if (!_d.License.Allowed) return Deny("license_blocked", box, deviceId, card, hash);
                var sn = _d.Stations.Get(box.StationId);
                if (sn == null || !EngineReady(sn)) return Deny("engine_offline", box, deviceId, card, hash);
                snap = sn;
                var t = _d.Trucks.Get(card.TruckId);
                if (t == null) return Deny("truck_not_found", box, deviceId, card, hash);
                truck = t;
                if (!_d.Trucks.CustomerExists(truck.Name)) return Deny("customer_not_found", box, deviceId, card, hash, truck.Name);
                op = _db.ActiveOpOfStation(box.StationId);
                if (op != null && op.TruckId != truck.Id) return Deny("station_busy", box, deviceId, card, hash);
                var other = _db.ActiveOpOfCustomer(truck.Name);
                if (other != null && other.StationId != box.StationId) return Deny("truck_busy", box, deviceId, card, hash);
                if (op == null && snap.SessionId != null && snap.SessionHasFlowed)
                {
                    var last = _db.LastClosedOpOfStation(box.StationId);
                    resume = last != null && last.TruckId == truck.Id && last.SessionId == snap.SessionId;
                }
            }

            double price, required;
            if (op != null) { price = op.Price; required = op.Required; }                      // انضمام: لا فحص مالي جديد
            else if (resume)
            {
                var last = _db.LastClosedOpOfStation(box.StationId)!;                           // استئناف نفس الجلسة: الفاتورة واحدة والكمية المعتمدة ثابتة
                price = last.Price; required = last.Required;
            }
            else
            {
                var p = _d.Price.BarrelPrice();
                if (p == null) return Deny("price_missing", box, deviceId, card, hash);
                price = p.Value; required = truck.Capacity * price;
                var bal = await _d.Balance.GetAsync(truck.Name, ct).ConfigureAwait(false);
                double pending = _d.Exposure.PendingAmount(truck.Name);
                string info = $"required={required:0.##} credit={bal.Credit:0.##} pending={pending:0.##} src={bal.Source} age={bal.AgeSec}s policy={_o.Policy}";
                if (_o.Policy != BalancePolicy.LogOnly)
                {
                    if (!bal.Available) return Deny("balance_unavailable", box, deviceId, card, hash, info);
                    double net = bal.Credit - pending;
                    bool ok = _o.Policy == BalancePolicy.Prepaid ? net >= required : net - required >= -_o.CreditLimit;
                    if (!ok) return Deny("insufficient_balance", box, deviceId, card, hash, info);
                }
                lock (_lock) Log("balance.checked", box.Id, deviceId, null, card.Id, info);
            }

            lock (_lock)
            {
                long now = Now;
                var op2 = _db.ActiveOpOfStation(box.StationId);                                  // أُعيد الفحص بعد انتظار الرصيد
                if (op2 != null && op2.TruckId != truck.Id) return Deny("station_busy", box, deviceId, card, hash);
                if (op2 == null)
                {
                    var other = _db.ActiveOpOfCustomer(truck.Name);
                    if (other != null && other.StationId != box.StationId) return Deny("truck_busy", box, deviceId, card, hash);
                }
                op = op2;
                if (op == null)
                {
                    op = _db.CreateOperation(box.StationId, truck.Id, truck.Name, card.Id, now, price, required, resume ? snap.SessionId : null, resume);
                    Log(resume ? "op.resume" : "op.start", box.Id, deviceId, op.Id, card.Id, $"required={required:0.##} price={price:0.##}");
                }
                else
                {
                    Log("op.join", box.Id, deviceId, op.Id, card.Id);
                    if (op.State == OpState.Locking)
                    {
                        foreach (var c in _db.OpenCommandsOfOp(op.Id).Where(x => x.Type == CommandType.Lock)) _db.SetCommandStatus(c.Id, CommandStatus.Cancelled);
                        op = op with { State = op.LastActiveMs > op.AuthMs ? OpState.Filling : OpState.Authorized };
                        _db.UpdateOp(op);
                    }
                }
                _db.AddOpBox(op.Id, box.Id);
                var des = _db.SetDesired(box.Id, LockState.Unlocked, now + _o.UnlockLeaseSec * 1000);
                string cmdId = NewId();
                var cmd = new Command(cmdId, deviceId, box.Id, CommandType.Unlock, des.Version, now, now + _o.UnlockCmdTtlSec * 1000, CommandStatus.Sent, 1, null, op.Id);
                _db.AddCommand(cmd);
                _db.AddPermit(new Permit(cmdId, box.Id, op.Id, truck.Id, now, now + _o.PermitTtlSec * 1000, null));
                _d.Control.ExpectTruck(box.StationId, truck.Id);
                Log("auth.ok", box.Id, deviceId, op.Id, card.Id, truck.Name);
                return new CheckResponse(true, "ok", ReasonAr["ok"], cmdId, Dto(cmd), now, "green");
            }
        }
        finally { _checkGate.Release(); }
    }

    // ------------------------------------------------------------ النبضة والتأكيد
    public HeartbeatResponse Heartbeat(string deviceId, LockState actual, bool readerOk, string fw)
    {
        lock (_lock)
        {
            long now = Now;
            var dev = _db.Device(deviceId);
            bool wasOffline = dev?.Offline == true;
            _db.UpdateDevice(deviceId, now, fw, readerOk, actual, false);
            if (wasOffline) Log("device.online", deviceId: deviceId);
            var box = _db.BoxOfDevice(deviceId);
            if (box == null) return new HeartbeatResponse(now, new DesiredDto("Locked", 0, 0), Array.Empty<CommandDto>());
            ApplyActual(box, actual, now);
            _db.ExpireCommands(now);
            var des = _db.GetDesired(box.Id);
            var op = _db.ActiveOpOfBox(box.Id);
            if (op != null && op.State != OpState.Locking && des.State == LockState.Unlocked)
                des = _db.SetDesired(box.Id, LockState.Unlocked, now + _o.UnlockLeaseSec * 1000);     // تجديد الإيجار طالما الجسر حيّ والعملية جارية
            var cmds = _db.OpenCommands(deviceId, now);
            foreach (var pc in cmds.Where(x => x.Status == CommandStatus.Pending)) _db.SetCommandStatus(pc.Id, CommandStatus.Sent);
            return new HeartbeatResponse(now, new DesiredDto(des.State.ToString(), des.Version, des.LeaseUntilMs), cmds.Select(Dto).ToList());
        }
    }

    public AckResponse Ack(string deviceId, string cmdId, bool executed, LockState actual)
    {
        lock (_lock)
        {
            long now = Now;
            var c = _db.CommandById(cmdId);
            if (c == null || c.DeviceId != deviceId) { Log("ack.unknown", null, deviceId, data: cmdId); return new AckResponse(false, "unknown_command"); }
            if (c.Status == CommandStatus.Acked) return new AckResponse(true, "already");
            if (!executed)
            {
                _db.SetCommandStatus(cmdId, CommandStatus.Failed, now);
                Log("fault.command_failed", c.BoxId, deviceId, c.OpId, data: c.Type.ToString());
                return new AckResponse(true, "noted");
            }
            if (now > c.ExpiresMs + 10_000) Log("fault.late_ack", c.BoxId, deviceId, c.OpId, data: c.Type.ToString());   // الواقع يغلب: نعتمد الحالة الفعلية
            _db.SetCommandStatus(cmdId, CommandStatus.Acked, now);
            if (c.Type == CommandType.Unlock) _db.UsePermit(cmdId, now);
            var box = _db.BoxById(c.BoxId);
            if (box != null) { _db.UpdateDevice(deviceId, now, _db.Device(deviceId)?.Fw ?? "", _db.Device(deviceId)?.ReaderOk ?? false, actual, false); ApplyActual(box, actual, now); }
            return new AckResponse(true, "ok");
        }
    }

    /// <summary>أحداث مخزّنة محليًا على الجهاز وقت انقطاع الاتصال؛ المكرر (جهاز+تسلسل) يُتجاهل. يعيد عدد الجديد.</summary>
    public int UploadEvents(string deviceId, IEnumerable<(long Seq, long Ts, string Type, string Data)> events)
    {
        int n = 0;
        lock (_lock)
        {
            var box = _db.BoxOfDevice(deviceId);
            foreach (var e in events)
            {
                if (!_db.TryRecordDeviceEvent(deviceId, e.Seq)) continue;
                _db.Log(e.Ts, "dev." + e.Type, box?.Id, deviceId, null, null, e.Data);
                n++;
            }
        }
        return n;
    }

    /// <summary>الحالة الفعلية للقفل تغلب: تُحدَّث العملية وأوامرها وتصاريحها بما أبلغ به الجهاز.</summary>
    private void ApplyActual(Box box, LockState actual, long now)
    {
        var op = _db.ActiveOpOfBox(box.Id);
        var des = _db.GetDesired(box.Id);
        if (actual == LockState.Unlocked)
        {
            if (op != null && des.State == LockState.Unlocked)
            {
                var ob = _db.OpBoxes(op.Id).FirstOrDefault(x => x.BoxId == box.Id);
                if (ob != null && ob.UnlockedMs == null) _db.SetOpBoxUnlocked(op.Id, box.Id, now);
                foreach (var c in _db.OpenCommandsOfOp(op.Id).Where(x => x.BoxId == box.Id && x.Type == CommandType.Unlock && x.Version <= des.Version))
                { _db.SetCommandStatus(c.Id, CommandStatus.Acked, now); _db.UsePermit(c.Id, now); }
            }
        }
        else if (actual == LockState.Locked)
        {
            if (des.State != LockState.Locked)
            {
                if (op != null) LogCool($"lu:{box.Id}", 60_000, "box.locked_unexpected", box.Id, box.DeviceId, op.Id);   // انتهى الإيجار أو إعادة تشغيل الجهاز
                return;
            }
            foreach (var c in _db.OpenCommands(box.DeviceId, now).Where(x => x.BoxId == box.Id && x.Type == CommandType.Lock))
                _db.SetCommandStatus(c.Id, CommandStatus.Acked, now);
            if (op != null)
            {
                var ob = _db.OpBoxes(op.Id).FirstOrDefault(x => x.BoxId == box.Id);
                if (ob != null && ob.LockedMs == null) { _db.SetOpBoxLocked(op.Id, box.Id, now); Log("box.locked", box.Id, box.DeviceId, op.Id); }
                TryClose(op, now);
            }
        }
    }

    private void TryClose(Operation op, long now)
    {
        if (op.State != OpState.Locking) return;
        if (_db.OpBoxes(op.Id).Any(x => x.LockedMs == null)) return;
        _db.UpdateOp(op with { State = OpState.Closed, ClosedMs = now });
        Log("op.closed", null, null, op.Id, null, op.CloseReason);
    }

    // ------------------------------------------------------------ المقيِّم الدوري (كل ثانية)
    public void Tick()
    {
        lock (_lock)
        {
            long now = Now;
            _db.ExpireCommands(now);
            foreach (var id in _db.DeviceIds())
            {
                var dev = _db.Device(id);
                if (dev != null && dev.LastSeenMs > 0 && !dev.Offline && now - dev.LastSeenMs > _o.OfflineSec * 1000)
                { _db.SetDeviceOffline(id, true); Log("device.offline", deviceId: id); }
            }
            bool graceOver = now >= _startedMs + _o.RestartGraceMin * 60_000L;
            foreach (var op in _db.ActiveOps()) TickOp(op, now, graceOver);
            DetectUnauthorizedFlow(now);
            DetectStuckUnlocked(now);
        }
    }

    private void TickOp(Operation op, long now, bool graceOver)
    {
        var snap = _d.Stations.Get(op.StationId);
        var opBoxes = _db.OpBoxes(op.Id);
        var boxes = opBoxes.Select(ob => _db.BoxById(ob.BoxId)).Where(b => b != null).Select(b => b!).ToList();
        var meters = boxes.Select(b => b.MeterIndex).Distinct().ToList();
        MeterView? Mv(int i) => snap?.Meters.FirstOrDefault(m => m.MeterIndex == i);
        bool engineOk = snap != null && EngineReady(snap);
        bool known = engineOk && meters.All(i => Mv(i) is { } mv && mv.LastValidAgeSec <= _o.StateMaxAgeSec);
        bool anyActive = engineOk && meters.Any(i => Mv(i)?.Active == true);

        if (snap?.SessionId != null && op.SessionId == null && (anyActive || snap.SessionHasFlowed)) op = op with { SessionId = snap.SessionId };

        if (anyActive)
        {
            bool wasLocking = op.State == OpState.Locking;
            op = op with { LastActiveMs = now, State = OpState.Filling };
            _db.UpdateOp(op);
            if (wasLocking) CancelLocking(op, boxes, now, "عاد التدفق");
            return;
        }

        if (op.State == OpState.Locking) { HandleLocking(op, boxes, now, known); return; }
        _db.UpdateOp(op);                                      // قد يكون SessionId تغيّر
        if (!graceOver || !known) return;                     // حالة مجهولة أو إعادة تشغيل حديثة: لا قفل

        string? reason = null;
        if (op.State == OpState.Filling)
        {
            bool sessionDone = snap!.State is FillState.Paused or FillState.Completed || snap.SessionId == null
                               || (op.SessionId != null && snap.SessionId != op.SessionId);
            if (sessionDone) reason = "انتهت التعبئة";
        }
        else if (op.State == OpState.Authorized && now - op.AuthMs >= _o.UnusedPermitSec * 1000) reason = "تصريح غير مستعمل";
        if (reason != null) BeginLocking(op, boxes, now, reason);
    }

    private void IssueLock(Operation op, Box box, long now, int attempts)
    {
        var des = _db.SetDesired(box.Id, LockState.Locked, 0);
        _db.AddCommand(new Command(NewId(), box.DeviceId, box.Id, CommandType.Lock, des.Version, now, now + _o.LockCmdTtlSec * 1000, CommandStatus.Pending, attempts, null, op.Id));
    }

    private void BeginLocking(Operation op, List<Box> boxes, long now, string reason)
    {
        foreach (var b in boxes) IssueLock(op, b, now, 1);
        _db.UpdateOp(op with { State = OpState.Locking, CloseReason = reason });
        Log("op.locking", null, null, op.Id, null, reason);
    }

    private void CancelLocking(Operation op, List<Box> boxes, long now, string why)
    {
        foreach (var c in _db.OpenCommandsOfOp(op.Id).Where(x => x.Type == CommandType.Lock)) _db.SetCommandStatus(c.Id, CommandStatus.Cancelled);
        foreach (var b in boxes)
            if (_db.OpBoxes(op.Id).FirstOrDefault(x => x.BoxId == b.Id)?.LockedMs == null) _db.SetDesired(b.Id, LockState.Unlocked, now + _o.UnlockLeaseSec * 1000);
        Log("op.lock_cancelled", null, null, op.Id, null, why);
    }

    private void HandleLocking(Operation op, List<Box> boxes, long now, bool known)
    {
        TryClose(op, now);
        var cur = _db.Op(op.Id);
        if (cur == null || cur.State == OpState.Closed) return;
        var obs = _db.OpBoxes(op.Id);
        bool pending = obs.Any(x => x.LockedMs == null);
        if (!known && pending)                                // الحالة صارت مجهولة قبل اكتمال القفل: تراجع
        {
            CancelLocking(op, boxes, now, "حالة مجهولة");
            _db.UpdateOp(op with { State = op.LastActiveMs > op.AuthMs ? OpState.Filling : OpState.Authorized });
            return;
        }
        foreach (var b in boxes)
        {
            if (obs.FirstOrDefault(x => x.BoxId == b.Id)?.LockedMs != null) continue;
            var last = _db.CommandsOfBox(b.Id, CommandType.Lock).FirstOrDefault();
            bool stale = last == null || last.Status is CommandStatus.Expired or CommandStatus.Failed or CommandStatus.Cancelled
                         || (last.Status is CommandStatus.Pending or CommandStatus.Sent && now - last.IssuedMs > _o.LockRetrySec * 1000);
            if (!stale) continue;
            int attempts = last == null ? 0 : last.Attempts;
            if (attempts >= _o.LockMaxAttempts) { LogCool($"lu:{op.Id}:{b.Id}", 600_000, "fault.lock_unconfirmed", b.Id, b.DeviceId, op.Id, $"attempts={attempts}"); continue; }
            if (last != null && last.Status is CommandStatus.Pending or CommandStatus.Sent) _db.SetCommandStatus(last.Id, CommandStatus.Expired);
            IssueLock(op, b, now, attempts + 1);
        }
    }

    private void DetectUnauthorizedFlow(long now)
    {
        foreach (var st in _db.Stations())
        {
            var snap = _d.Stations.Get(st.Id);
            if (snap == null || !EngineReady(snap)) continue;
            foreach (var b in _db.BoxesOfStation(st.Id))
            {
                var mv = snap.Meters.FirstOrDefault(m => m.MeterIndex == b.MeterIndex);
                if (mv == null || !mv.Active) continue;
                bool authorized = _db.GetDesired(b.Id).State == LockState.Unlocked && _db.ActiveOpOfBox(b.Id) != null;
                if (!authorized) LogCool($"uf:{b.Id}", 60_000, "fault.unauthorized_flow", b.Id, b.DeviceId, null, "تدفق بلا تصريح");
            }
        }
    }

    private void DetectStuckUnlocked(long now)
    {
        foreach (var b in _db.Boxes())
        {
            var dev = _db.Device(b.DeviceId);
            if (dev == null || dev.Offline || dev.Actual != LockState.Unlocked) continue;
            if (_db.GetDesired(b.Id).State == LockState.Locked && _db.ActiveOpOfBox(b.Id) == null && now - dev.LastSeenMs < 30_000)
                LogCool($"su:{b.Id}", _o.StuckUnlockedSec * 1000, "fault.stuck_unlocked", b.Id, b.DeviceId, null, "الصندوق مفتوح والحالة المطلوبة مقفلة");
        }
    }
}
