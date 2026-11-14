using System.Globalization;
using System.Text.Json.Nodes;
using SmartWater.Data;

namespace SmartWater.Ebs;

public enum EnqueueStatus { Queued, AlreadyQueued, NoTruck, ZeroQuantity, TooRecent, UnknownCustomer, NoPrice }
public sealed record EnqueueResult(EnqueueStatus Status, InvoiceRow? Invoice, string Message);
public sealed record SyncOneResult(string LocalId, bool Success, string? Error = null, bool IsDuplicateWarning = false);

/// <summary>
/// من سجل زفة إلى فاتورة معلّقة: الوايت (اسمه = اسم عميل EBS) والكمية = الكمية المعتمدة للزفة (التي أدخلها المسؤول للوايت، بلا أي خصم
/// إضافي)، والسعر = «سعر البرميل الافتراضي» من إعدادات EBS. الإضافة للطابور محلية فقط ولا تتصل بالشبكة.
/// </summary>
public sealed class EbsInvoiceQueue
{
    /// <summary>الحد الأدنى لعمر آخر حركة قبل قبول الزفة: السجل يُحدَّث كل 15ث أثناء الجلسة وقد تتوقف 10 دقائق ثم تستأنف.</summary>
    public const long DefaultMinAgeMs = 11 * 60_000L;

    private readonly EbsDb _db;
    public EbsInvoiceQueue(EbsDb db) => _db = db;

    public EnqueueResult Enqueue(SwingRecord r, EbsSettings s, long nowMs, long minAgeMs = DefaultMinAgeMs)
    {
        var existing = _db.InvoiceOfSwing(r.Id);
        if (existing != null) return new(EnqueueStatus.AlreadyQueued, existing, "الزفة في طابور المزامنة مسبقًا");
        if (r.TruckId == null || string.IsNullOrWhiteSpace(r.TruckName))
            return new(EnqueueStatus.NoTruck, null, "الزفة غير مرتبطة بوايت (عميل) — اربطها أولًا");
        if (r.Total <= 0) return new(EnqueueStatus.ZeroQuantity, null, "الكمية صفر");
        if (nowMs - r.EndMs < minAgeMs) return new(EnqueueStatus.TooRecent, null, "الزفة حديثة وقد لا تكون التعبئة انتهت؛ أعد المحاولة بعد قليل");
        if (_db.CustomerCount() == 0) return new(EnqueueStatus.UnknownCustomer, null, "حمّل قائمة العملاء من EBS أولًا");
        if (_db.FindCustomer(r.TruckName) == null)
            return new(EnqueueStatus.UnknownCustomer, null, $"«{r.TruckName}» ليس في قائمة عملاء EBS — صحّح الاسم أو حمّل العملاء من جديد");
        if (!double.TryParse(s.DefaultBarrelPrice.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var price) || price < 0)
            return new(EnqueueStatus.NoPrice, null, "حدّد «سعر البرميل الافتراضي» في إعدادات EBS");
        var row = _db.AddInvoice(r.Id, EbsDb.Norm(r.TruckName), r.Total, price, "زفه", r.EndMs, nowMs);
        return row == null ? new(EnqueueStatus.AlreadyQueued, _db.InvoiceOfSwing(r.Id), "الزفة في طابور المزامنة مسبقًا")
                           : new(EnqueueStatus.Queued, row, "أُضيفت للطابور");
    }
}

/// <summary>
/// مزامنة الفواتير مع EBS (نقل لـ InvoiceSyncService.kt)، بنفس القواعد: الحفظ محليًا قبل أي إرسال؛ لا حذف بعد النجاح؛
/// لا إعادة إنشاء Master موجود ولا إعادة إرسال Details موجودة؛ «مزامنة الكل» بالترتيب وتتوقف عند أول فشل؛
/// نجاح حقيقي = HTTP + كود EBS 200 + معرّف سيرفر؛ ولا مزامنة تلقائية: لا تُستدعى إلا بطلب صريح من المستخدم.
/// الفرق عن الأصل: تحذير «سجل مكرر» لا يُؤكَّد تلقائيًا بل يتوقف عنده (NeedsConfirm) ما لم يُفعَّل autoConfirmDuplicates،
/// لأن انقطاعًا بعد وصول الطلب للسيرفر قد يجعل إعادة المحاولة تنشئ فاتورة ثانية.
/// </summary>
public sealed class InvoiceSyncService
{
    private const string MasterForm = "EBS.Mobile.Distribution.wndInv_SellInvoice";
    private const string DetailsForm = "EBS.Mobile.Distribution.wndInv_SellInvoiceDetails";
    private const string DuplicateText = "يوجد سجل له نفس بيانات هذا السجل";

    private readonly EbsDb _db;
    private readonly EbsClient _client;
    private readonly Func<EbsSettings> _settings;
    private readonly Func<long> _nowMs;
    private readonly bool _autoConfirmDuplicates;

    public InvoiceSyncService(EbsDb db, EbsClient client, Func<EbsSettings> settings, bool autoConfirmDuplicates = false, Func<long>? nowMs = null)
    {
        _db = db; _client = client; _settings = settings; _autoConfirmDuplicates = autoConfirmDuplicates;
        _nowMs = nowMs ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    private async Task<bool> EnsureLoggedIn(CancellationToken ct) =>
        !string.IsNullOrEmpty(_client.CurrentSessionId) || (await _client.LoginAsync(_settings(), ct).ConfigureAwait(false)).Success;

    private static bool IsDuplicate(string? msg) => msg != null && msg.Contains(DuplicateText);

    private static string Describe(EbsOperationResult r)
    {
        if (!r.HttpSuccess) return string.IsNullOrEmpty(r.EbsMessage) ? $"HTTP {r.HttpStatusCode} (تحقق من الرابط أو الاتصال)" : $"تعذّر الاتصال: {r.EbsMessage}";
        if (r.EbsCode != null && r.EbsCode != 200) return $"EBS code {r.EbsCode}: {r.EbsMessage}";
        return "فشل غير معروف";
    }

    private static string Raw500(string? raw) { raw ??= ""; return raw.Length > 500 ? raw[..500] + "..." : raw; }

    private InvoiceRow Save(InvoiceRow r) { _db.Update(r); return r; }

    /// <summary>يزامن فاتورة واحدة مستأنفًا من آخر مرحلة نجحت. confirmDuplicate=true بعد موافقة المستخدم على تحذير «سجل مكرر».</summary>
    public async Task<SyncOneResult> SyncOneAsync(string localId, bool confirmDuplicate = false, CancellationToken ct = default)
    {
        var rec = _db.Invoice(localId) ?? throw new ArgumentException("فاتورة غير موجودة: " + localId);
        if (rec.Status == InvoiceStatus.Synced) return new(localId, true);
        long now = _nowMs();
        if (!await EnsureLoggedIn(ct).ConfigureAwait(false))
        {
            Save(rec with { Status = InvoiceStatus.SyncFailed, LastError = "فشل تسجيل الدخول لـEBS", Attempts = rec.Attempts + 1, LastAttemptMs = now });
            return new(localId, false, "فشل تسجيل الدخول لـEBS");
        }

        var cur = Save(rec with { Status = InvoiceStatus.Syncing, Attempts = rec.Attempts + 1, LastAttemptMs = now });

        // --- الخطوة 1: Master (تُتخطّى إن نجحت سابقًا)
        if (cur.MasterId == null)
        {
            if (confirmDuplicate)
                cur = Save(cur with { Notes = cur.Notes.Contains("Same_Record_Exist") ? cur.Notes : (cur.Notes.Length == 0 ? "Same_Record_Exist" : cur.Notes + " Same_Record_Exist"), EbsNumericId = _db.NextNumericId(now) });
            var res = await _client.CallOperationAsync(MasterForm, new[] { MasterPayload(cur) }, ct: ct).ConfigureAwait(false);
            if (!res.IsFullSuccess)
            {
                bool dup = IsDuplicate(res.EbsMessage);
                if (dup && !confirmDuplicate && _autoConfirmDuplicates) return await SyncOneAsync(localId, true, ct).ConfigureAwait(false);
                string err = Describe(res);
                if (dup) { Save(cur with { Status = InvoiceStatus.NeedsConfirm, LastError = err + " — إن كانت هذه إعادة محاولة بعد انقطاع فقد تكون الفاتورة وصلت؛ راجع EBS قبل التأكيد" }); }
                else Save(cur with { Status = InvoiceStatus.SyncFailed, LastError = "Master: " + err });
                return new(localId, false, err, dup);
            }
            long mid = EbsJson.ExtractServerId(res.Root);
            if (mid == 0)
            {
                var msg = "نجح Master لكن بدون معرّف سيرفر. الرد الخام: " + Raw500(res.RawResponse);
                Save(cur with { Status = InvoiceStatus.SyncFailed, LastError = msg });
                return new(localId, false, msg);
            }
            cur = Save(cur with { Status = InvoiceStatus.MasterSuccess, MasterId = mid, LastError = null });
        }

        // --- الخطوة 2: Details (تُتخطّى إن نجحت سابقًا)
        if (cur.DetailsId == null)
        {
            cur = Save(cur with { Status = InvoiceStatus.DetailsPending });
            var res = await _client.CallOperationAsync(DetailsForm, new[] { DetailsPayload(cur, cur.MasterId!.Value) }, ct: ct).ConfigureAwait(false);
            if (!res.IsFullSuccess)
            {
                var err = Describe(res);
                Save(cur with { Status = InvoiceStatus.SyncFailed, LastError = "Details: " + err });   // Master نجح فلن يُعاد إنشاؤه
                return new(localId, false, err);
            }
            long did = EbsJson.ExtractServerId(res.Root);
            if (did == 0)
            {
                var msg = "نجح Details لكن بدون معرّف سيرفر. الرد الخام: " + Raw500(res.RawResponse);
                Save(cur with { Status = InvoiceStatus.SyncFailed, LastError = msg });
                return new(localId, false, msg);
            }
            cur = Save(cur with { Status = InvoiceStatus.DetailsSuccess, DetailsId = did });
        }

        Save(cur with { Status = InvoiceStatus.Synced, LastError = null });
        return new(localId, true);
    }

    /// <summary>«مزامنة الكل بالترتيب»: من الأقدم للأحدث وتتوقف عند أول فشل (بما فيه انتظار تأكيد مكرر).</summary>
    public async Task<IReadOnlyList<SyncOneResult>> SyncAllOrderedAsync(CancellationToken ct = default)
    {
        var results = new List<SyncOneResult>();
        foreach (var inv in _db.Invoices(pendingOnly: true))
        {
            var r = await SyncOneAsync(inv.LocalId, false, ct).ConfigureAwait(false);
            results.Add(r);
            if (!r.Success) break;
        }
        return results;
    }

    // ------------------------------------------------------------ الحمولات (حرفية من BuildMasterPayload/BuildDetailsPayload)
    private static string D(long ms, string fmt) => DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString(fmt, CultureInfo.InvariantCulture);

    private JsonObject MasterPayload(InvoiceRow r)
    {
        var s = _settings();
        string theDate = D(r.CreatedMs, "yyyy-MM-dd");
        string posting = D(r.CreatedMs + 6L * 30 * 24 * 60 * 60 * 1000, "yyyy-MM-dd");
        string customer = string.IsNullOrWhiteSpace(r.CustomerName) ? "غير معروف" : r.CustomerName;
        return new JsonObject
        {
            ["اسم الحساب"] = customer, ["العنوان"] = null, ["عدد المرفقات"] = "0", ["الفرع"] = s.BranchName, ["قيد البضاعة"] = null,
            ["مركز التكلفة"] = "", ["العملة"] = s.Currency, ["الزبون"] = customer, ["Deleted"] = "0", ["مناولة"] = "0", ["الخصم"] = 0.0,
            ["الموزع"] = s.UserName, ["السائق"] = "0", ["رقم السائق"] = null, ["وقت الإدخال"] = theDate, ["سعر الصرف"] = 1.0,
            ["سعر التحويل لعملة المخزون"] = 1, ["خصم1"] = null, ["ID"] = 0, ["معفي من الضريبة"] = false, ["Latitude"] = 0.0, ["Location"] = "",
            ["Longitude"] = 0.0, ["تاريخ الترحيل"] = posting, ["رقم الجوال"] = "", ["الفاتورة غير قابلة للإرجاع"] = false,
            ["ملاحظات"] = string.IsNullOrEmpty(r.Notes) ? "زفه" : r.Notes, ["رقم طلب الشراء"] = "0", ["عدد الطبعات"] = 0, ["رقم المرجع"] = "",
            ["RowVersion"] = 0, ["خدمات المبيعات"] = null, ["تفاصيل خدمات المبيعات"] = null, ["ضريبة المبيعات"] = null, ["خصم2"] = null,
            ["حساب المبيعات"] = "المبيعات", ["نوع الفاتورة"] = "فاتورة بيع", ["المخزن"] = s.DefaultStoreName, ["التاريخ"] = theDate,
            ["الدفع"] = s.DefaultPaymentType, ["الرقم"] = 0, ["TheNumberFromApp"] = r.EbsNumericId, ["TheYear"] = s.Year.ToString(CultureInfo.InvariantCulture),
            ["خصم3"] = null, ["الإجمالي"] = r.Total, ["ضريبة القيمه المضافه"] = "0", ["TxtError"] = null, ["المستخدم"] = s.UserName,
            ["VisitingStatusID"] = 0, ["isSynError"] = false, ["isSynchronization"] = false,
            // "Num" مُغفَل عمدًا: رقم تسلسل يعيّنه السيرفر للسجلات الجديدة
        };
    }

    private JsonObject DetailsPayload(InvoiceRow r, long parentId)
    {
        var s = _settings();
        return new JsonObject
        {
            ["رقم الدفعه"] = 0.0, ["البونص"] = 0.0, ["الفرع"] = s.BranchName, ["وقت الإدخال"] = D(r.CreatedMs, "yyyy/MM/dd"), ["العبوة"] = 1.0,
            ["تاريخ الانتهاء"] = "", ["اختصار تاريخ الانتهاء"] = "", ["ن.خصم1"] = null, ["ID"] = 0, ["IsCheck"] = false, ["اسم الصنف"] = "مياة",
            ["رقم الصنف"] = "11001", ["ParentID"] = parentId, ["الكمية"] = r.Barrels, ["الخصم نسبة"] = null, ["RowVersion"] = 0, ["ن.خصم2"] = "0",
            ["الخصم"] = 0.0, ["ملاحظات"] = "", ["SubRefNumber"] = null, ["المخزن"] = s.DefaultStoreName, ["قيمة الضريبه"] = 0.0, ["الرقم التسلسلي"] = 1,
            ["TheNumberFromApp"] = r.EbsNumericId, ["الإجمالي"] = r.Total, ["TxtError"] = null, ["الوحدة"] = s.WaterUnitName, ["سعر الوحدة"] = r.Price,
            ["المستخدم"] = s.UserName, ["الباركود"] = "", ["isSynError"] = false, ["isSynchronization"] = false, ["الماركة"] = null,
        };
    }
}
