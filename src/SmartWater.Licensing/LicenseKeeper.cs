namespace SmartWater.Licensing;

/// <summary>
/// حالة الترخيص على الجهاز: يحفظ مفتاح التفعيل وآخر مستند وصل، ويحكم بساعة موثوقة. عند انقطاع الاتصال يبقى آخر حكم
/// معروف (لا يُوقَف أحد بسبب الشبكة). إن لم تتوفر ساعة موثوقة نأخذ الأكبر من ساعة الجهاز وآخر وقت خادم رآه.
/// </summary>
public sealed class LicenseKeeper
{
    private const string KId = "lic_id", KDoc = "lic_doc", KClock = "lic_clk", KServer = "lic_srv",
                         KContact = "lic_contact", KRevoked = "lic_rev", KDev = "lic_dev";

    private readonly IKeyValue _kv;
    private readonly Func<long> _wall;
    private readonly TrustedClock _clock;

    public LicenseKeeper(IKeyValue kv, Func<long> wall, Func<long> mono, Func<string>? bootId = null)
    {
        _kv = kv; _wall = wall;
        _clock = new TrustedClock(wall, mono, bootId);
        _clock.Restore(kv.Get(KClock));
    }

    private long Long(string k) => long.TryParse(_kv.Get(k), out var v) ? v : 0L;

    public string? LicenseId() => LicenseKey.Normalize(_kv.Get(KId));

    /// <summary>يحفظ مفتاحًا جديدًا ويمحو حالة المفتاح السابق.</summary>
    public void SetLicenseId(string id)
    {
        _kv.Put(KId, id); _kv.Put(KDoc, null); _kv.Put(KRevoked, null); _kv.Put(KDev, null);
    }

    public void Clear()
    {
        _kv.Put(KId, null); _kv.Put(KDoc, null); _kv.Put(KRevoked, null); _kv.Put(KDev, null);
    }

    public LicenseDoc? Doc() => LicenseCodec.Decode(_kv.Get(KDoc));
    public bool Revoked() => _kv.Get(KRevoked) == "1";
    public bool DeviceOk() => _kv.Get(KDev) != "0";
    public long LastContactMs() => Long(KContact);

    public long NowMs()
    {
        var r = _clock.Read();
        return r.Trusted ? r.Ms : Math.Max(_wall(), Long(KServer));
    }

    /// <summary>
    /// نتيجة جلب ناجحة: raw نص المستند (null = لا مستند على الخادم)، serverMs وقت الخادم، deviceOk ترتيب هذا الجهاز.
    /// نص موجود لكنه غير صالح لا يستبدل المحفوظ (لا نوقف أحدًا بسبب نص تالف).
    /// </summary>
    public void OnFetched(string? raw, long serverMs, bool? deviceOk = null)
    {
        if (serverMs > 0)
        {
            _clock.AnchorNow(serverMs);
            _kv.Put(KClock, _clock.Encode());
            _kv.Put(KServer, Math.Max(serverMs, Long(KServer)).ToString());
            _kv.Put(KContact, serverMs.ToString());
        }
        if (raw == null) { _kv.Put(KRevoked, "1"); _kv.Put(KDoc, null); }
        else if (LicenseCodec.Decode(raw) != null) { _kv.Put(KDoc, raw.Trim()); _kv.Put(KRevoked, null); }
        if (deviceOk != null) _kv.Put(KDev, deviceOk.Value ? "1" : "0");
    }

    /// <param name="required">false لبناء تطوير داخلي لا يطلب ترخيصًا (يعمل بلا مفتاح).</param>
    public LicenseVerdict Verdict(bool required = true)
    {
        if (LicenseId() == null) return required ? LicenseVerdict.NotActivated : LicenseVerdict.Unmanaged;
        long now = NowMs();
        long contact = LastContactMs();
        return LicenseGate.Evaluate(Doc(), now, contact == 0 ? now : contact, DeviceOk(), Revoked());
    }
}
