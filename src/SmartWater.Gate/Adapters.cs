using System.Globalization;
using SmartWater.Data;
using SmartWater.Ebs;
using SmartWater.Engine;
using SmartWater.Licensing;

namespace SmartWater.Gate;

/// <summary>حالة محطة من المحرك الحي (قراءة فقط): نشاط كل عدّاد وعمر آخر قراءة سليمة وحالة جلسة التعبئة.</summary>
public sealed class EngineStationView : IStationView
{
    private readonly MonitorEngine _e; private readonly GateDb _db;
    public EngineStationView(MonitorEngine e, GateDb db) { _e = e; _db = db; }

    public StationSnapshot? Get(int stationId)
    {
        var meters = _db.BoxesOfStation(stationId).Select(b => b.MeterIndex).Distinct().ToList();
        if (meters.Count == 0) return null;
        var s = _e.SessionInfo();
        return new StationSnapshot(stationId, _e.Running, _e.Connected, _e.FrameAgeSec,
            meters.Select(i => new MeterView(i, _e.IsMeterActive(i), _e.MeterValidAgeSec(i))).ToList(), s.SessionId, s.State, s.HasFlowed);
    }
}

public sealed class EngineStationControl : IStationControl
{
    private readonly MonitorEngine _e;
    public EngineStationControl(MonitorEngine e) => _e = e;
    public void ExpectTruck(int stationId, long truckId) => _e.ExpectTruck(truckId);
}

public sealed class TruckDirectory : ITruckDirectory
{
    private readonly TruckDb _trucks; private readonly EbsDb _ebs;
    public TruckDirectory(TruckDb trucks, EbsDb ebs) { _trucks = trucks; _ebs = ebs; }
    public TruckInfo? Get(long truckId) => _trucks.Get(truckId) is { } t ? new TruckInfo(t.Id, t.Name, t.Capacity) : null;
    public bool CustomerExists(string name) => _ebs.FindCustomer(name) != null;
}

public sealed class EbsPriceProvider : IPriceProvider
{
    private readonly Func<EbsSettings> _s;
    public EbsPriceProvider(Func<EbsSettings> s) => _s = s;
    public double? BarrelPrice() =>
        double.TryParse(_s().DefaultBarrelPrice.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var p) && p >= 0 ? p : null;
}

/// <summary>الالتزامات التي لا يراها EBS بعد: فواتير الطابور غير المرسلة + زفات مربوطة بالوايت لم تدخل الطابور (بالكمية المعتمدة × السعر).</summary>
public sealed class EbsExposureProvider : IExposureProvider
{
    private readonly SwingDb _swings; private readonly EbsDb _ebs; private readonly Func<EbsSettings> _s;
    public EbsExposureProvider(SwingDb swings, EbsDb ebs, Func<EbsSettings> s) { _swings = swings; _ebs = ebs; _s = s; }

    public double PendingAmount(string customerName)
    {
        string n = EbsDb.Norm(customerName).ToLowerInvariant();
        double queued = _ebs.Invoices(pendingOnly: true).Where(i => EbsDb.Norm(i.CustomerName).ToLowerInvariant() == n).Sum(i => i.Total);
        double.TryParse(_s().DefaultBarrelPrice.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var price);
        double unqueued = _swings.Query(null)
            .Where(r => r.TruckId != null && EbsDb.Norm(r.TruckName).ToLowerInvariant() == n && _ebs.InvoiceOfSwing(r.Id) == null)
            .Sum(r => r.Total * price);
        return queued + unqueued;
    }
}

/// <summary>رصيد العميل: قراءة حيّة من EBS بمهلة قصيرة، وإلا اللقطة المحمَّلة بشرط ألا يتجاوز عمرها الحد؛ وإلا «غير متاح».</summary>
public sealed class EbsBalanceProvider : IBalanceProvider
{
    private readonly EbsDb _db; private readonly Func<EbsClient> _client; private readonly Func<EbsSettings> _settings;
    private readonly GateOptions _o; private readonly IClock _clock; private readonly TimeSpan _liveTimeout;

    public EbsBalanceProvider(EbsDb db, Func<EbsClient> client, Func<EbsSettings> settings, GateOptions o, IClock clock, TimeSpan? liveTimeout = null)
    { _db = db; _client = client; _settings = settings; _o = o; _clock = clock; _liveTimeout = liveTimeout ?? TimeSpan.FromSeconds(4); }

    public async Task<BalanceResult> GetAsync(string customerName, CancellationToken ct)
    {
        var s = _settings();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_liveTimeout);
            var c = _client();
            if (string.IsNullOrEmpty(c.CurrentSessionId))
            {
                var lg = await c.LoginAsync(s, cts.Token).ConfigureAwait(false);
                if (!lg.Success) throw new InvalidOperationException(lg.ErrorMessage);
            }
            var res = await c.ReadAccountBalanceAsync(customerName, cts.Token).ConfigureAwait(false);
            var r = EbsBalanceParser.Parse(res, s.Currency);
            if (r.Ok && !r.Empty) return new BalanceResult(true, r.Credit, "live", 0);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { /* ننتقل إلى اللقطة المحمَّلة */ }

        var cust = _db.FindCustomer(customerName);
        long upd = _db.BalancesUpdatedMs();
        if (cust?.AccountId is long acc && upd > 0)
        {
            long age = (_clock.NowMs - upd) / 1000;
            if (age <= _o.SnapshotMaxAgeMin * 60)
            {
                var credit = _db.SnapshotCredit(acc, _db.CurrencyId(s.Currency));
                if (credit != null) return new BalanceResult(true, credit.Value, "snapshot", age);
            }
        }
        return new BalanceResult(false, 0, "none", 0);
    }
}

public sealed class LicenseGateAdapter : ILicenseGate
{
    private readonly LicenseManager _l;
    public LicenseGateAdapter(LicenseManager l) => _l = l;
    public bool Allowed => _l.Allowed;
}
