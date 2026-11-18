namespace SmartWater.Gate;

public enum CardStatus { Active, Revoked }
public enum LockState { Locked, Unlocked, Unknown }
public enum OpState { Authorized, Filling, Locking, Closed }
public enum CommandType { Unlock, Lock }
public enum CommandStatus { Pending, Sent, Acked, Failed, Expired, Cancelled }
public enum BalancePolicy { Prepaid, CreditLimit, LogOnly }

public sealed record Station(int Id, string Name, bool Series);

/// <summary>صندوق ← حنفية ← عدّاد ← محطة. جهاز ESP32 واحد لكل صندوق.</summary>
public sealed record Box(int Id, string Name, int StationId, int TapNo, int MeterIndex, string DeviceId, bool Enabled);

public sealed record DeviceRow(string DeviceId, long LastSeenMs, long LastSeq, string Fw, bool ReaderOk, LockState Actual, bool Offline, bool Enabled);

public sealed record Card(long Id, string UidHash, string Label, string Driver, long TruckId, CardStatus Status, long CreatedMs, long? RevokedMs);

public sealed record Operation(long Id, int StationId, long TruckId, string Customer, long CardId, long AuthMs, double Price, double Required,
                               string? SessionId, OpState State, string CloseReason, long? ClosedMs, long LastActiveMs, bool Resumed);

public sealed record OpBox(long OpId, int BoxId, long? UnlockedMs, long? LockedMs);

public sealed record Permit(string Id, int BoxId, long OpId, long TruckId, long CreatedMs, long ExpiresMs, long? UsedMs);

public sealed record Command(string Id, string DeviceId, int BoxId, CommandType Type, long Version, long IssuedMs, long ExpiresMs,
                             CommandStatus Status, int Attempts, long? AckMs, long OpId);

/// <summary>الحالة المطلوبة للقفل. LeaseUntilMs: لا يبقى مفتوحًا بعد هذا الوقت ما لم يُجدَّد (حماية عند توقف الجسر).</summary>
public sealed record Desired(int BoxId, LockState State, long Version, long LeaseUntilMs);

public sealed record GateEvent(long Id, long TsMs, string Kind, int? BoxId, string? DeviceId, long? OpId, long? CardId, string Data);

public sealed class GateOptions
{
    public long PermitTtlSec { get; init; } = 60;
    public long UnlockCmdTtlSec { get; init; } = 15;
    public long LockCmdTtlSec { get; init; } = 300;
    /// <summary>إيجار الفتح: يتجدد مع كل نبضة طالما العملية جارية؛ عند توقف الجسر ينتهي فيُغلق الجهاز ذاتيًا (الوضع الآمن).</summary>
    public long UnlockLeaseSec { get; init; } = 120;
    public double StateMaxAgeSec { get; init; } = 5;
    public double FrameMaxAgeSec { get; init; } = 10;
    public long UnusedPermitSec { get; init; } = 600;
    public long LockRetrySec { get; init; } = 60;
    public int LockMaxAttempts { get; init; } = 3;
    public long OfflineSec { get; init; } = 20;
    public long RestartGraceMin { get; init; } = 15;
    public long StuckUnlockedSec { get; init; } = 600;
    public int MaxDeniedPerMin { get; init; } = 5;
    public BalancePolicy Policy { get; init; } = BalancePolicy.Prepaid;
    public double CreditLimit { get; init; }
    public long SnapshotMaxAgeMin { get; init; } = 60;
    public byte[] CardSecret { get; init; } = new byte[32];
    public long AuthSkewSec { get; init; } = 30;
}

// ---------- DTOs للبروتوكول (camelCase في JSON)
public sealed record CommandDto(string CmdId, string Type, long Version, long ExpiresMs);
public sealed record DesiredDto(string State, long Version, long LeaseUntilMs);
public sealed record CheckResponse(bool Allowed, string Reason, string ReasonAr, string? PermitId, CommandDto? Command, long ServerTimeMs, string Led);
public sealed record HeartbeatResponse(long ServerTimeMs, DesiredDto Desired, IReadOnlyList<CommandDto> Commands);
public sealed record AckResponse(bool Accepted, string Note);

// ---------- واجهات تعتمد عليها الخدمة (تُحاكى في الاختبارات)
public interface IClock { long NowMs { get; } }
public sealed class SystemClock : IClock { public long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); }

public sealed record MeterView(int MeterIndex, bool Active, double LastValidAgeSec);
public sealed record StationSnapshot(int StationId, bool EngineRunning, bool CameraConnected, double FrameAgeSec,
                                     IReadOnlyList<MeterView> Meters, string? SessionId, SmartWater.Core.FillState? State, bool SessionHasFlowed);

public interface IStationView { StationSnapshot? Get(int stationId); }
public interface IStationControl { void ExpectTruck(int stationId, long truckId); }

public sealed record TruckInfo(long Id, string Name, double Capacity);
public interface ITruckDirectory
{
    TruckInfo? Get(long truckId);
    bool CustomerExists(string name);
}

/// <param name="Credit">موجب = للعميل رصيد (دائن)، سالب = عليه دين (مدين)</param>
public sealed record BalanceResult(bool Available, double Credit, string Source, long AgeSec);
public interface IBalanceProvider { Task<BalanceResult> GetAsync(string customerName, CancellationToken ct); }

/// <summary>مجموع الفواتير/الزفات غير المرسلة إلى EBS لهذا العميل (لا يراها EBS بعد). عرض فقط وليس رصيدًا موازيًا.</summary>
public interface IExposureProvider { double PendingAmount(string customerName); }
public interface IPriceProvider { double? BarrelPrice(); }
public interface ILicenseGate { bool Allowed { get; } }
