using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using SmartWater.Core;

namespace SmartWater.Data;

public sealed record Truck(long Id, string Name, double Capacity, int Photos = 0);

/// <summary>خطأ إدخال (اسم/كمية غير صالحة)؛ الرسالة جاهزة للعرض للمستخدم.</summary>
public sealed class TruckInputError : Exception { public TruckInputError(string msg) : base(msg) { } }

/// <summary>
/// قائمة الوايتات المحفوظة محليًا (SQLite، بلا إنترنت): الاسم + الكمية الكاملة بالبراميل + صور التعلّم وبصماتها.
/// نفس مخطط نسخة أندرويد. الاسم فريد بلا اعتبار لحالة الأحرف. آمنة للخيوط (قفل واحد).
/// </summary>
public sealed class TruckDb : IDisposable
{
    public const int MaxPhotosPerTruck = 10;
    private readonly SqliteConnection _db;
    private readonly object _lock = new();

    public TruckDb(string path)
    {
        _db = Sql.Open(path);
        if (Sql.UserVersion(_db) < 1)
        {
            Sql.Exec(_db, "CREATE TABLE IF NOT EXISTS trucks(id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL COLLATE NOCASE UNIQUE, capacity REAL NOT NULL, created_ms INTEGER NOT NULL)");
            Sql.Exec(_db, "CREATE TABLE IF NOT EXISTS truck_photos(id INTEGER PRIMARY KEY AUTOINCREMENT, truck_id INTEGER NOT NULL, path TEXT NOT NULL, feat BLOB NOT NULL, created_ms INTEGER NOT NULL)");
            Sql.Exec(_db, "CREATE INDEX IF NOT EXISTS idx_truck_photos_truck ON truck_photos(truck_id)");
            Sql.SetUserVersion(_db, 1);
        }
    }

    private static string Clean(string name, double capacity)
    {
        var n = Regex.Replace(name.Trim(), @"\s+", " ");
        if (n.Length == 0) throw new TruckInputError("اكتب اسم الوايت");
        if (!double.IsFinite(capacity) || capacity <= 0.0) throw new TruckInputError("اكتب كمية صحيحة بالبراميل (أكبر من صفر)");
        if (capacity > 1000.0) throw new TruckInputError("الكمية كبيرة جدًا — تأكد أنها بالبراميل");
        return n;
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private static bool IsConstraint(SqliteException e) => e.SqliteErrorCode == 19;

    public Truck Add(string name, double capacity)
    {
        var n = Clean(name, capacity);
        lock (_lock)
        {
            try
            {
                long id = Sql.InsertId(_db, "INSERT INTO trucks(name,capacity,created_ms) VALUES(@n,@c,@t)", ("@n", n), ("@c", capacity), ("@t", Now()));
                return new Truck(id, n, capacity, 0);
            }
            catch (SqliteException e) when (IsConstraint(e)) { throw new TruckInputError($"الاسم «{n}» موجود مسبقًا"); }
        }
    }

    /// <summary>تعديل الاسم/الكمية. الكمية الجديدة تُطبَّق على الزفات القادمة فقط.</summary>
    public Truck Update(long id, string name, double capacity)
    {
        var n = Clean(name, capacity);
        lock (_lock)
        {
            int rows;
            try { rows = Sql.Exec(_db, "UPDATE trucks SET name=@n, capacity=@c WHERE id=@id", ("@n", n), ("@c", capacity), ("@id", id)); }
            catch (SqliteException e) when (IsConstraint(e)) { throw new TruckInputError($"الاسم «{n}» موجود مسبقًا"); }
            if (rows == 0) throw new TruckInputError("الوايت غير موجود (ربما حُذف)");
            return GetUnlocked(id)!;
        }
    }

    /// <summary>ملفات الصور تبقى على القرص (سجلات قديمة قد تشير إليها)؛ التنظيف الدوري يحذف القديم منها.</summary>
    public void Delete(long id)
    {
        lock (_lock)
        {
            Sql.Exec(_db, "DELETE FROM truck_photos WHERE truck_id=@id", ("@id", id));
            Sql.Exec(_db, "DELETE FROM trucks WHERE id=@id", ("@id", id));
        }
    }

    private const string ListSql = "SELECT t.id,t.name,t.capacity,(SELECT COUNT(*) FROM truck_photos p WHERE p.truck_id=t.id) FROM trucks t";
    private static Truck ReadTruck(SqliteDataReader r) => new(r.GetInt64(0), r.GetString(1), r.GetDouble(2), r.GetInt32(3));

    public IReadOnlyList<Truck> List()
    {
        lock (_lock) return Sql.Query(_db, ListSql + " ORDER BY t.name COLLATE NOCASE", ReadTruck);
    }

    public Truck? Get(long id) { lock (_lock) return GetUnlocked(id); }
    private Truck? GetUnlocked(long id) => Sql.One(_db, ListSql + " WHERE t.id=@id", ReadTruck, ("@id", id));

    /// <summary>يضيف صورة تعلّم مع بصمتها، ويحذف الأقدم إن تجاوزت الحد. نفس الصورة لا تُضاف مرتين.</summary>
    public void AddPhoto(long truckId, string path, float[] feat)
    {
        lock (_lock)
        {
            bool exists = Sql.Query(_db, "SELECT 1 FROM truck_photos WHERE truck_id=@t AND path=@p", r => 1, ("@t", truckId), ("@p", path)).Count > 0;
            if (exists) return;
            Sql.Exec(_db, "INSERT INTO truck_photos(truck_id,path,feat,created_ms) VALUES(@t,@p,@f,@c)",
                ("@t", truckId), ("@p", path), ("@f", TruckMatcher.ToBytes(feat)), ("@c", Now()));
            Sql.Exec(_db, $"DELETE FROM truck_photos WHERE truck_id=@t AND id NOT IN (SELECT id FROM truck_photos WHERE truck_id=@t ORDER BY id DESC LIMIT {MaxPhotosPerTruck})", ("@t", truckId));
        }
    }

    public IReadOnlyList<TruckSample> Samples()
    {
        lock (_lock)
        {
            var raw = Sql.Query(_db, "SELECT truck_id,feat FROM truck_photos", r => (r.GetInt64(0), (byte[])r.GetValue(1)));
            var o = new List<TruckSample>();
            foreach (var (id, blob) in raw) { var f = TruckMatcher.FromBytes(blob); if (f != null) o.Add(new TruckSample(id, f)); }
            return o;
        }
    }

    public IReadOnlyList<string> PhotoPaths(long truckId)
    {
        lock (_lock) return Sql.Query(_db, "SELECT path FROM truck_photos WHERE truck_id=@t ORDER BY id DESC", r => r.GetString(0), ("@t", truckId));
    }

    public ISet<string> AllPhotoPaths()
    {
        lock (_lock) return Sql.Query(_db, "SELECT path FROM truck_photos", r => r.GetString(0)).ToHashSet();
    }

    public void Dispose() => _db.Dispose();
}
