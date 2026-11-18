using SmartWater.Core;
using SmartWater.Gate;

namespace SmartWater.Tests;

internal sealed class FakeClock : IClock
{
    public long NowMs { get; set; } = 1_800_000_000_000L;
    public void Advance(double sec) => NowMs += (long)(sec * 1000);
}

internal sealed class FakeStations : IStationView, IStationControl
{
    public Dictionary<int, StationSnapshot> Snaps = new();
    public List<(int Station, long Truck)> Expected = new();
    public StationSnapshot? Get(int id) => Snaps.TryGetValue(id, out var s) ? s : null;
    public void ExpectTruck(int stationId, long truckId) => Expected.Add((stationId, truckId));
}

internal sealed class FakeTrucks : ITruckDirectory
{
    public Dictionary<long, TruckInfo> Trucks = new();
    public HashSet<string> Customers = new();
    public TruckInfo? Get(long id) => Trucks.TryGetValue(id, out var t) ? t : null;
    public bool CustomerExists(string name) => Customers.Contains(name);
}

internal sealed class FakeBalance : IBalanceProvider
{
    public BalanceResult Result = new(true, 200_000, "live", 0);
    public int Calls;
    public Task<BalanceResult> GetAsync(string customerName, CancellationToken ct) { Calls++; return Task.FromResult(Result); }
}

internal sealed class FakeExposure : IExposureProvider { public double Pending; public double PendingAmount(string customerName) => Pending; }
internal sealed class FakePrice : IPriceProvider { public double? Price = 2000; public double? BarrelPrice() => Price; }
internal sealed class FakeLicense : ILicenseGate { public bool Ok = true; public bool Allowed => Ok; }

/// <summary>
/// محطتان: المحطة 1 فيها صندوقان (عدّاد 0 و1) والمحطة 2 فيها صندوق واحد. وايتان (55 و40 برميلًا) وبطاقتان.
/// السعر 2000، الرصيد 200000. محرك وهمي يتحكم الاختبار بحالته.
/// </summary>
internal sealed class GateHarness : IDisposable
{
    public readonly GateDb Db = new(":memory:");
    public readonly FakeClock Clock = new();
    public readonly FakeStations Stations = new();
    public readonly FakeTrucks Trucks = new();
    public readonly FakeBalance Balance = new();
    public readonly FakeExposure Exposure = new();
    public readonly FakePrice Price = new();
    public readonly FakeLicense License = new();
    public GateService Svc;
    public GateOptions Options;
    public readonly Dictionary<string, byte[]> Keys = new();
    public const long T1 = 1, T2 = 2;
    public const string Name1 = "وايت أبو علي", Name2 = "وايت 2";

    public GateHarness(GateOptions? opts = null)
    {
        Options = opts ?? new GateOptions { RestartGraceMin = 0, CardSecret = new byte[32] };
        Trucks.Trucks[T1] = new TruckInfo(T1, Name1, 55);
        Trucks.Trucks[T2] = new TruckInfo(T2, Name2, 40);
        Trucks.Customers.Add(Name1); Trucks.Customers.Add(Name2);
        Db.UpsertStation(new Station(1, "محطة 1", false));
        Db.UpsertStation(new Station(2, "محطة 2", false));
        Db.UpsertBox(new Box(1, "صندوق 1", 1, 1, 0, "ESP1", true));
        Db.UpsertBox(new Box(2, "صندوق 2", 1, 2, 1, "ESP2", true));
        Db.UpsertBox(new Box(3, "صندوق 3", 2, 1, 0, "ESP3", true));
        Svc = MakeService();
        foreach (var d in new[] { "ESP1", "ESP2", "ESP3" }) Keys[d] = Svc.AddDevice(d);
        Svc.RegisterCard("AA11", "بطاقة 1", "سائق 1", T1);
        Svc.RegisterCard("BB22", "بطاقة 2", "سائق 2", T2);
        Idle(1); Idle(2);
    }

    public GateService MakeService() => new(Db, Options, new GateDeps(Stations, Stations, Trucks, Balance, Exposure, Price, License, Clock, new PlainKeyProtector()));

    /// <summary>يحاكي «إعادة تشغيل الجسر»: خدمة جديدة على نفس القاعدة.</summary>
    public void RestartBridge() => Svc = MakeService();

    public void Set(int station = 1, bool m0 = false, bool m1 = false, FillState? state = null, string? session = null, bool flowed = false,
                    double age = 0.5, bool engine = true, bool camera = true, double frameAge = 0.5)
        => Stations.Snaps[station] = new StationSnapshot(station, engine, camera, frameAge,
            new[] { new MeterView(0, m0, age), new MeterView(1, m1, age) }, session, state, flowed);

    public void Idle(int station) => Set(station);

    public async Task<CheckResponse> Tap(string device, string uid, bool ack = true)
    {
        var r = await Svc.CheckAsync(device, uid);
        if (r.Allowed && ack) Svc.Ack(device, r.Command!.CmdId, true, LockState.Unlocked);
        return r;
    }

    public HeartbeatResponse Hb(string device, LockState actual) => Svc.Heartbeat(device, actual, true, "t1");

    /// <summary>يمرّر الوقت ويشغّل المقيِّم، مع نبضات من الأجهزة المذكورة (وتطبيق أوامر القفل كما يفعل الجهاز).</summary>
    public void Run(double seconds, params string[] devices)
    {
        for (double t = 0; t < seconds; t += 1)
        {
            Clock.Advance(1);
            Svc.Tick();
            foreach (var d in devices) Device(d);
        }
    }

    /// <summary>جهاز يعمل كما في الواقع: نبضة، وتنفيذ أي أمر قفل/فتح، وتأكيده.</summary>
    public void Device(string dev)
    {
        var cur = Db.Device(dev)!.Actual;
        var hb = Hb(dev, cur == LockState.Unknown ? LockState.Locked : cur);
        foreach (var c in hb.Commands)
        {
            var target = c.Type == "unlock" ? LockState.Unlocked : LockState.Locked;
            Svc.Ack(dev, c.CmdId, true, target);
        }
    }

    public IReadOnlyList<Command> LockCommands(int boxId) => Db.CommandsOfBox(boxId, CommandType.Lock);
    public bool Has(string kind) => Db.Events(500, kind).Count > 0;
    public void Dispose() => Db.Dispose();
}
