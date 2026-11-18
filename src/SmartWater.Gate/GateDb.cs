using Microsoft.Data.Sqlite;
using SmartWater.Data;

namespace SmartWater.Gate;

/// <summary>
/// قاعدة WaterGateBridge (gate.db): الصناديق والمحطات والأجهزة والبطاقات والعمليات والتصاريح والأوامر والأحداث فقط.
/// لا عملاء ولا أرصدة ولا أسعار ولا كميات محاسبية ولا زفات (هذه تبقى في swings.db وtrucks.db وEBS).
/// </summary>
public sealed class GateDb : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly object _lock = new();

    public GateDb(string path)
    {
        _db = Sql.Open(path);
        if (Sql.UserVersion(_db) < 1)
        {
            Sql.Exec(_db, "CREATE TABLE IF NOT EXISTS stations(id INTEGER PRIMARY KEY, name TEXT NOT NULL, series INTEGER NOT NULL)");
            Sql.Exec(_db, "CREATE TABLE IF NOT EXISTS boxes(id INTEGER PRIMARY KEY, name TEXT NOT NULL, station_id INTEGER NOT NULL, tap_no INTEGER NOT NULL, meter_index INTEGER NOT NULL, device_id TEXT NOT NULL UNIQUE, enabled INTEGER NOT NULL)");
            Sql.Exec(_db, "CREATE TABLE IF NOT EXISTS devices(device_id TEXT PRIMARY KEY, key_protected BLOB NOT NULL, last_seen_ms INTEGER NOT NULL DEFAULT 0, last_seq INTEGER NOT NULL DEFAULT 0, fw TEXT NOT NULL DEFAULT '', reader_ok INTEGER NOT NULL DEFAULT 0, actual TEXT NOT NULL DEFAULT 'Unknown', offline INTEGER NOT NULL DEFAULT 0, enabled INTEGER NOT NULL DEFAULT 1)");
            Sql.Exec(_db, "CREATE TABLE IF NOT EXISTS cards(id INTEGER PRIMARY KEY AUTOINCREMENT, uid_hash TEXT NOT NULL UNIQUE, label TEXT NOT NULL, driver TEXT NOT NULL, truck_id INTEGER NOT NULL, status TEXT NOT NULL, created_ms INTEGER NOT NULL, revoked_ms INTEGER)");
            Sql.Exec(_db, "CREATE TABLE IF NOT EXISTS operations(id INTEGER PRIMARY KEY AUTOINCREMENT, station_id INTEGER NOT NULL, truck_id INTEGER NOT NULL, customer TEXT NOT NULL, card_id INTEGER NOT NULL, auth_ms INTEGER NOT NULL, price REAL NOT NULL, required REAL NOT NULL, session_id TEXT, state TEXT NOT NULL, close_reason TEXT NOT NULL DEFAULT '', closed_ms INTEGER, last_active_ms INTEGER NOT NULL DEFAULT 0, resumed INTEGER NOT NULL DEFAULT 0)");
            Sql.Exec(_db, "CREATE TABLE IF NOT EXISTS op_boxes(op_id INTEGER NOT NULL, box_id INTEGER NOT NULL, unlocked_ms INTEGER, locked_ms INTEGER, PRIMARY KEY(op_id, box_id))");
            Sql.Exec(_db, "CREATE TABLE IF NOT EXISTS permits(id TEXT PRIMARY KEY, box_id INTEGER NOT NULL, op_id INTEGER NOT NULL, truck_id INTEGER NOT NULL, created_ms INTEGER NOT NULL, expires_ms INTEGER NOT NULL, used_ms INTEGER)");
            Sql.Exec(_db, "CREATE TABLE IF NOT EXISTS commands(id TEXT PRIMARY KEY, device_id TEXT NOT NULL, box_id INTEGER NOT NULL, type TEXT NOT NULL, version INTEGER NOT NULL, issued_ms INTEGER NOT NULL, expires_ms INTEGER NOT NULL, status TEXT NOT NULL, attempts INTEGER NOT NULL DEFAULT 0, ack_ms INTEGER, op_id INTEGER NOT NULL)");
            Sql.Exec(_db, "CREATE TABLE IF NOT EXISTS desired(box_id INTEGER PRIMARY KEY, state TEXT NOT NULL, version INTEGER NOT NULL, lease_until_ms INTEGER NOT NULL)");
            Sql.Exec(_db, "CREATE TABLE IF NOT EXISTS events(id INTEGER PRIMARY KEY AUTOINCREMENT, ts_ms INTEGER NOT NULL, kind TEXT NOT NULL, box_id INTEGER, device_id TEXT, op_id INTEGER, card_id INTEGER, data TEXT NOT NULL DEFAULT '')");
            Sql.Exec(_db, "CREATE TABLE IF NOT EXISTS dev_seq(device_id TEXT NOT NULL, seq INTEGER NOT NULL, PRIMARY KEY(device_id, seq))");
            Sql.Exec(_db, "CREATE INDEX IF NOT EXISTS idx_events_ts ON events(ts_ms)");
            Sql.Exec(_db, "CREATE INDEX IF NOT EXISTS idx_ops_state ON operations(state)");
            Sql.SetUserVersion(_db, 1);
        }
    }

    private static T P<T>(string s) where T : struct => Enum.Parse<T>(s);

    // ------------------------------------------------------------ المحطات والصناديق
    public void UpsertStation(Station s)
    {
        lock (_lock) Sql.Exec(_db, "INSERT OR REPLACE INTO stations(id,name,series) VALUES(@i,@n,@s)", ("@i", s.Id), ("@n", s.Name), ("@s", s.Series ? 1 : 0));
    }

    public IReadOnlyList<Station> Stations()
    {
        lock (_lock) return Sql.Query(_db, "SELECT id,name,series FROM stations ORDER BY id", r => new Station(r.GetInt32(0), r.GetString(1), r.GetInt32(2) == 1));
    }

    public void UpsertBox(Box b)
    {
        lock (_lock)
            Sql.Exec(_db, "INSERT OR REPLACE INTO boxes(id,name,station_id,tap_no,meter_index,device_id,enabled) VALUES(@i,@n,@s,@t,@m,@d,@e)",
                ("@i", b.Id), ("@n", b.Name), ("@s", b.StationId), ("@t", b.TapNo), ("@m", b.MeterIndex), ("@d", b.DeviceId), ("@e", b.Enabled ? 1 : 0));
    }

    private static Box ReadBox(SqliteDataReader r) => new(r.GetInt32(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4), r.GetString(5), r.GetInt32(6) == 1);
    private const string BoxCols = "id,name,station_id,tap_no,meter_index,device_id,enabled";

    public IReadOnlyList<Box> Boxes() { lock (_lock) return Sql.Query(_db, $"SELECT {BoxCols} FROM boxes ORDER BY id", ReadBox); }
    public Box? BoxById(int id) { lock (_lock) return Sql.One(_db, $"SELECT {BoxCols} FROM boxes WHERE id=@i", ReadBox, ("@i", id)); }
    public Box? BoxOfDevice(string deviceId) { lock (_lock) return Sql.One(_db, $"SELECT {BoxCols} FROM boxes WHERE device_id=@d", ReadBox, ("@d", deviceId)); }
    public IReadOnlyList<Box> BoxesOfStation(int stationId) { lock (_lock) return Sql.Query(_db, $"SELECT {BoxCols} FROM boxes WHERE station_id=@s ORDER BY id", ReadBox, ("@s", stationId)); }

    // ------------------------------------------------------------ الأجهزة
    public void RegisterDevice(string deviceId, byte[] protectedKey)
    {
        lock (_lock)
            Sql.Exec(_db, "INSERT INTO devices(device_id,key_protected) VALUES(@d,@k) ON CONFLICT(device_id) DO UPDATE SET key_protected=@k, last_seq=0",
                ("@d", deviceId), ("@k", protectedKey));
    }

    public byte[]? DeviceKey(string deviceId)
    {
        lock (_lock)
        {
            var l = Sql.Query(_db, "SELECT key_protected FROM devices WHERE device_id=@d AND enabled=1", r => (byte[])r.GetValue(0), ("@d", deviceId));
            return l.Count > 0 ? l[0] : null;
        }
    }

    public DeviceRow? Device(string deviceId)
    {
        lock (_lock)
            return Sql.One(_db, "SELECT device_id,last_seen_ms,last_seq,fw,reader_ok,actual,offline,enabled FROM devices WHERE device_id=@d",
                r => new DeviceRow(r.GetString(0), r.GetInt64(1), r.GetInt64(2), r.GetString(3), r.GetInt32(4) == 1, P<LockState>(r.GetString(5)), r.GetInt32(6) == 1, r.GetInt32(7) == 1),
                ("@d", deviceId));
    }

    public IReadOnlyList<string> DeviceIds() { lock (_lock) return Sql.Query(_db, "SELECT device_id FROM devices ORDER BY device_id", r => r.GetString(0)); }

    public void SetDeviceSeq(string deviceId, long seq) { lock (_lock) Sql.Exec(_db, "UPDATE devices SET last_seq=@s WHERE device_id=@d", ("@s", seq), ("@d", deviceId)); }
    public void SetDeviceEnabled(string deviceId, bool on) { lock (_lock) Sql.Exec(_db, "UPDATE devices SET enabled=@e WHERE device_id=@d", ("@e", on ? 1 : 0), ("@d", deviceId)); }

    public void UpdateDevice(string deviceId, long lastSeenMs, string fw, bool readerOk, LockState actual, bool offline)
    {
        lock (_lock)
            Sql.Exec(_db, "UPDATE devices SET last_seen_ms=@t, fw=@f, reader_ok=@r, actual=@a, offline=@o WHERE device_id=@d",
                ("@t", lastSeenMs), ("@f", fw), ("@r", readerOk ? 1 : 0), ("@a", actual.ToString()), ("@o", offline ? 1 : 0), ("@d", deviceId));
    }

    public void SetDeviceOffline(string deviceId, bool offline) { lock (_lock) Sql.Exec(_db, "UPDATE devices SET offline=@o WHERE device_id=@d", ("@o", offline ? 1 : 0), ("@d", deviceId)); }

    // ------------------------------------------------------------ البطاقات
    private const string CardCols = "id,uid_hash,label,driver,truck_id,status,created_ms,revoked_ms";
    private static Card ReadCard(SqliteDataReader r) => new(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt64(4), P<CardStatus>(r.GetString(5)), r.GetInt64(6), r.IsDBNull(7) ? null : r.GetInt64(7));

    public Card AddCard(string uidHash, string label, string driver, long truckId, long nowMs)
    {
        lock (_lock)
        {
            long id = Sql.InsertId(_db, "INSERT INTO cards(uid_hash,label,driver,truck_id,status,created_ms) VALUES(@h,@l,@d,@t,'Active',@c)",
                ("@h", uidHash), ("@l", label), ("@d", driver), ("@t", truckId), ("@c", nowMs));
            return new Card(id, uidHash, label, driver, truckId, CardStatus.Active, nowMs, null);
        }
    }

    public Card? CardByHash(string hash) { lock (_lock) return Sql.One(_db, $"SELECT {CardCols} FROM cards WHERE uid_hash=@h", ReadCard, ("@h", hash)); }
    public Card? CardById(long id) { lock (_lock) return Sql.One(_db, $"SELECT {CardCols} FROM cards WHERE id=@i", ReadCard, ("@i", id)); }
    public IReadOnlyList<Card> Cards() { lock (_lock) return Sql.Query(_db, $"SELECT {CardCols} FROM cards ORDER BY id", ReadCard); }
    public void RevokeCard(long id, long nowMs) { lock (_lock) Sql.Exec(_db, "UPDATE cards SET status='Revoked', revoked_ms=@t WHERE id=@i", ("@t", nowMs), ("@i", id)); }

    // ------------------------------------------------------------ العمليات
    private const string OpCols = "id,station_id,truck_id,customer,card_id,auth_ms,price,required,session_id,state,close_reason,closed_ms,last_active_ms,resumed";
    private static Operation ReadOp(SqliteDataReader r) => new(r.GetInt64(0), r.GetInt32(1), r.GetInt64(2), r.GetString(3), r.GetInt64(4), r.GetInt64(5),
        r.GetDouble(6), r.GetDouble(7), r.IsDBNull(8) ? null : r.GetString(8), P<OpState>(r.GetString(9)), r.GetString(10), r.IsDBNull(11) ? null : r.GetInt64(11),
        r.GetInt64(12), r.GetInt32(13) == 1);

    public Operation CreateOperation(int stationId, long truckId, string customer, long cardId, long nowMs, double price, double required, string? sessionId, bool resumed)
    {
        lock (_lock)
        {
            long id = Sql.InsertId(_db,
                "INSERT INTO operations(station_id,truck_id,customer,card_id,auth_ms,price,required,session_id,state,last_active_ms,resumed) VALUES(@s,@t,@c,@card,@a,@p,@r,@sess,'Authorized',@a,@res)",
                ("@s", stationId), ("@t", truckId), ("@c", customer), ("@card", cardId), ("@a", nowMs), ("@p", price), ("@r", required), ("@sess", sessionId), ("@res", resumed ? 1 : 0));
            return Op(id)!;
        }
    }

    public Operation? Op(long id) { lock (_lock) return Sql.One(_db, $"SELECT {OpCols} FROM operations WHERE id=@i", ReadOp, ("@i", id)); }
    public IReadOnlyList<Operation> ActiveOps() { lock (_lock) return Sql.Query(_db, $"SELECT {OpCols} FROM operations WHERE state<>'Closed' ORDER BY id", ReadOp); }
    public Operation? ActiveOpOfStation(int stationId) { lock (_lock) return Sql.One(_db, $"SELECT {OpCols} FROM operations WHERE station_id=@s AND state<>'Closed' ORDER BY id DESC", ReadOp, ("@s", stationId)); }
    public Operation? ActiveOpOfCustomer(string customer) { lock (_lock) return Sql.One(_db, $"SELECT {OpCols} FROM operations WHERE customer=@c COLLATE NOCASE AND state<>'Closed' ORDER BY id DESC", ReadOp, ("@c", customer)); }
    public Operation? LastClosedOpOfStation(int stationId) { lock (_lock) return Sql.One(_db, $"SELECT {OpCols} FROM operations WHERE station_id=@s AND state='Closed' ORDER BY id DESC", ReadOp, ("@s", stationId)); }
    public IReadOnlyList<Operation> Operations(int limit = 200) { lock (_lock) return Sql.Query(_db, $"SELECT {OpCols} FROM operations ORDER BY id DESC LIMIT {limit}", ReadOp); }

    public Operation? ActiveOpOfBox(int boxId)
    {
        lock (_lock)
            return Sql.One(_db, $"SELECT {OpCols} FROM operations WHERE state<>'Closed' AND id IN (SELECT op_id FROM op_boxes WHERE box_id=@b) ORDER BY id DESC", ReadOp, ("@b", boxId));
    }

    public void UpdateOp(Operation o)
    {
        lock (_lock)
            Sql.Exec(_db, "UPDATE operations SET session_id=@s, state=@st, close_reason=@cr, closed_ms=@cm, last_active_ms=@la WHERE id=@i",
                ("@s", o.SessionId), ("@st", o.State.ToString()), ("@cr", o.CloseReason), ("@cm", o.ClosedMs), ("@la", o.LastActiveMs), ("@i", o.Id));
    }

    public void AddOpBox(long opId, int boxId) { lock (_lock) Sql.Exec(_db, "INSERT OR IGNORE INTO op_boxes(op_id,box_id) VALUES(@o,@b)", ("@o", opId), ("@b", boxId)); }

    public IReadOnlyList<OpBox> OpBoxes(long opId)
    {
        lock (_lock)
            return Sql.Query(_db, "SELECT op_id,box_id,unlocked_ms,locked_ms FROM op_boxes WHERE op_id=@o ORDER BY box_id",
                r => new OpBox(r.GetInt64(0), r.GetInt32(1), r.IsDBNull(2) ? null : r.GetInt64(2), r.IsDBNull(3) ? null : r.GetInt64(3)), ("@o", opId));
    }

    public void SetOpBoxUnlocked(long opId, int boxId, long ms) { lock (_lock) Sql.Exec(_db, "UPDATE op_boxes SET unlocked_ms=@m, locked_ms=NULL WHERE op_id=@o AND box_id=@b", ("@m", ms), ("@o", opId), ("@b", boxId)); }
    public void SetOpBoxLocked(long opId, int boxId, long ms) { lock (_lock) Sql.Exec(_db, "UPDATE op_boxes SET locked_ms=@m WHERE op_id=@o AND box_id=@b", ("@m", ms), ("@o", opId), ("@b", boxId)); }

    // ------------------------------------------------------------ التصاريح
    public void AddPermit(Permit p)
    {
        lock (_lock)
            Sql.Exec(_db, "INSERT INTO permits(id,box_id,op_id,truck_id,created_ms,expires_ms) VALUES(@i,@b,@o,@t,@c,@e)",
                ("@i", p.Id), ("@b", p.BoxId), ("@o", p.OpId), ("@t", p.TruckId), ("@c", p.CreatedMs), ("@e", p.ExpiresMs));
    }

    public Permit? PermitById(string id)
    {
        lock (_lock)
            return Sql.One(_db, "SELECT id,box_id,op_id,truck_id,created_ms,expires_ms,used_ms FROM permits WHERE id=@i",
                r => new Permit(r.GetString(0), r.GetInt32(1), r.GetInt64(2), r.GetInt64(3), r.GetInt64(4), r.GetInt64(5), r.IsDBNull(6) ? null : r.GetInt64(6)), ("@i", id));
    }

    public bool UsePermit(string id, long ms) { lock (_lock) return Sql.Exec(_db, "UPDATE permits SET used_ms=@m WHERE id=@i AND used_ms IS NULL", ("@m", ms), ("@i", id)) > 0; }

    // ------------------------------------------------------------ الأوامر والحالة المطلوبة
    private const string CmdCols = "id,device_id,box_id,type,version,issued_ms,expires_ms,status,attempts,ack_ms,op_id";
    private static Command ReadCmd(SqliteDataReader r) => new(r.GetString(0), r.GetString(1), r.GetInt32(2), P<CommandType>(r.GetString(3)), r.GetInt64(4), r.GetInt64(5), r.GetInt64(6),
        P<CommandStatus>(r.GetString(7)), r.GetInt32(8), r.IsDBNull(9) ? null : r.GetInt64(9), r.GetInt64(10));

    public void AddCommand(Command c)
    {
        lock (_lock)
            Sql.Exec(_db, $"INSERT INTO commands({CmdCols}) VALUES(@i,@d,@b,@t,@v,@is,@ex,@st,@at,NULL,@o)",
                ("@i", c.Id), ("@d", c.DeviceId), ("@b", c.BoxId), ("@t", c.Type.ToString()), ("@v", c.Version), ("@is", c.IssuedMs), ("@ex", c.ExpiresMs),
                ("@st", c.Status.ToString()), ("@at", c.Attempts), ("@o", c.OpId));
    }

    public Command? CommandById(string id) { lock (_lock) return Sql.One(_db, $"SELECT {CmdCols} FROM commands WHERE id=@i", ReadCmd, ("@i", id)); }

    public void SetCommandStatus(string id, CommandStatus st, long? ackMs = null)
    {
        lock (_lock) Sql.Exec(_db, "UPDATE commands SET status=@s, ack_ms=COALESCE(@a, ack_ms) WHERE id=@i", ("@s", st.ToString()), ("@a", ackMs), ("@i", id));
    }

    public IReadOnlyList<Command> OpenCommands(string deviceId, long nowMs)
    {
        lock (_lock)
            return Sql.Query(_db, $"SELECT {CmdCols} FROM commands WHERE device_id=@d AND status IN ('Pending','Sent') AND expires_ms>@n ORDER BY issued_ms",
                ReadCmd, ("@d", deviceId), ("@n", nowMs));
    }

    public IReadOnlyList<Command> OpenCommandsOfOp(long opId)
    {
        lock (_lock) return Sql.Query(_db, $"SELECT {CmdCols} FROM commands WHERE op_id=@o AND status IN ('Pending','Sent') ORDER BY issued_ms", ReadCmd, ("@o", opId));
    }

    public IReadOnlyList<Command> CommandsOfBox(int boxId, CommandType type)
    {
        lock (_lock) return Sql.Query(_db, $"SELECT {CmdCols} FROM commands WHERE box_id=@b AND type=@t ORDER BY issued_ms DESC", ReadCmd, ("@b", boxId), ("@t", type.ToString()));
    }

    public int ExpireCommands(long nowMs)
    {
        lock (_lock) return Sql.Exec(_db, "UPDATE commands SET status='Expired' WHERE status IN ('Pending','Sent') AND expires_ms<=@n", ("@n", nowMs));
    }

    public Desired GetDesired(int boxId)
    {
        lock (_lock)
        {
            var d = Sql.One(_db, "SELECT box_id,state,version,lease_until_ms FROM desired WHERE box_id=@b",
                r => new Desired(r.GetInt32(0), P<LockState>(r.GetString(1)), r.GetInt64(2), r.GetInt64(3)), ("@b", boxId));
            return d ?? new Desired(boxId, LockState.Locked, 0, 0);
        }
    }

    /// <summary>يضبط الحالة المطلوبة؛ يزيد الإصدار فقط عند تغيّر الحالة، وإلا يجدّد الإيجار بنفس الإصدار.</summary>
    public Desired SetDesired(int boxId, LockState state, long leaseUntilMs)
    {
        lock (_lock)
        {
            var cur = GetDesired(boxId);
            long ver = cur.State == state && cur.Version > 0 ? cur.Version : cur.Version + 1;
            Sql.Exec(_db, "INSERT OR REPLACE INTO desired(box_id,state,version,lease_until_ms) VALUES(@b,@s,@v,@l)",
                ("@b", boxId), ("@s", state.ToString()), ("@v", ver), ("@l", leaseUntilMs));
            return new Desired(boxId, state, ver, leaseUntilMs);
        }
    }

    // ------------------------------------------------------------ الأحداث والأعطال
    public void Log(long tsMs, string kind, int? boxId = null, string? deviceId = null, long? opId = null, long? cardId = null, string data = "")
    {
        lock (_lock)
            Sql.Exec(_db, "INSERT INTO events(ts_ms,kind,box_id,device_id,op_id,card_id,data) VALUES(@t,@k,@b,@d,@o,@c,@x)",
                ("@t", tsMs), ("@k", kind), ("@b", boxId), ("@d", deviceId), ("@o", opId), ("@c", cardId), ("@x", data));
    }

    /// <summary>true إن كان الحدث جديدًا (جهاز + تسلسل فريد)؛ false = مكرر فيُتجاهل.</summary>
    public bool TryRecordDeviceEvent(string deviceId, long seq)
    {
        lock (_lock) return Sql.Exec(_db, "INSERT OR IGNORE INTO dev_seq(device_id,seq) VALUES(@d,@s)", ("@d", deviceId), ("@s", seq)) > 0;
    }

    public IReadOnlyList<GateEvent> Events(int limit = 200, string? kindPrefix = null)
    {
        lock (_lock)
        {
            string where = kindPrefix == null ? "" : " WHERE kind LIKE @p";
            var args = kindPrefix == null ? Array.Empty<(string, object?)>() : new (string, object?)[] { ("@p", kindPrefix + "%") };
            return Sql.Query(_db, $"SELECT id,ts_ms,kind,box_id,device_id,op_id,card_id,data FROM events{where} ORDER BY id DESC LIMIT {limit}",
                r => new GateEvent(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetInt32(3), r.IsDBNull(4) ? null : r.GetString(4),
                    r.IsDBNull(5) ? null : r.GetInt64(5), r.IsDBNull(6) ? null : r.GetInt64(6), r.GetString(7)), args);
        }
    }

    public void Dispose() => _db.Dispose();
}
