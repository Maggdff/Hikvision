using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using SmartWater.Core;

namespace SmartWater.Data;

/// <param name="TruckId">الوايت المرتبط (null = لم يُحدَّد بعد)</param>
/// <param name="Photo">مسار صورة الوايت الملتقطة عند بدء الحركة (فارغ إن لم تُلتقط)</param>
/// <param name="Measured">الكمية التي قاستها العدّادات (Total تصبح كمية الوايت المدخلة عند ربطه)</param>
/// <param name="Duplicate">وُجدت زفة مسجّلة تغطي العملية نفسها فلم تُسجَّل ثانيةً؛ هذا هو السجل الموجود</param>
/// <param name="SessionId">معرّف جلسة التعبئة: سجل واحد لكل جلسة يُحدَّث ولا يتكرر</param>
public sealed record SwingRecord(
    long Id, string Day, int Seq, bool Full, string Meters, long StartMs, long EndMs,
    double M1Barrels, double M2Barrels, double Total, string Notes,
    long? TruckId = null, string TruckName = "", string Photo = "", double Measured = 0.0,
    bool Duplicate = false, string SessionId = "")
{
    public string TypeAr => Full ? "زفة كاملة" : "زفة عداد";
}

/// <summary>سجل الزفات (SQLite): رقم الزفة اليومي ونوعها ووقتها وكمية البراميل (+ الوايت المرتبط وصورته). نفس مخطط أندرويد (v3).</summary>
public sealed class SwingDb : IDisposable
{
    private const string Cols = "id,day,seq,full,meters,start_ms,end_ms,m1,m2,total,notes,truck_id,truck_name,photo,measured,session_id";
    private readonly SqliteConnection _db;
    private readonly object _lock = new();

    public SwingDb(string path)
    {
        _db = Sql.Open(path);
        int v = Sql.UserVersion(_db);
        if (v < 3)
        {
            // قاعدة جديدة: المخطط الكامل. (قاعدة قديمة بإصدار أقل تُرقّى بإضافة الأعمدة فقط.)
            Sql.Exec(_db, "CREATE TABLE IF NOT EXISTS swings(id INTEGER PRIMARY KEY AUTOINCREMENT, day TEXT NOT NULL, seq INTEGER NOT NULL," +
                "full INTEGER NOT NULL, meters TEXT NOT NULL, start_ms INTEGER, end_ms INTEGER, m1 REAL, m2 REAL, total REAL, notes TEXT," +
                "truck_id INTEGER, truck_name TEXT, photo TEXT, measured REAL, session_id TEXT)");
            foreach (var col in new[] { "truck_id INTEGER", "truck_name TEXT", "photo TEXT", "measured REAL", "session_id TEXT" })
            {
                try { Sql.Exec(_db, $"ALTER TABLE swings ADD COLUMN {col}"); } catch (SqliteException) { /* موجود */ }
            }
            Sql.Exec(_db, "CREATE UNIQUE INDEX IF NOT EXISTS idx_swings_session ON swings(session_id) WHERE session_id IS NOT NULL");
            Sql.SetUserVersion(_db, 3);
        }
    }

    private static string DayOf(long ms) =>
        DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string TimeOf(long ms) =>
        DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    public string Today() => DayOf(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    private static SwingRecord Read(SqliteDataReader c)
    {
        double total = Sql.Dbl(c, 9);
        return new SwingRecord(
            c.GetInt64(0), Sql.Str(c, 1), c.GetInt32(2), c.GetInt32(3) == 1, Sql.Str(c, 4),
            c.IsDBNull(5) ? 0 : c.GetInt64(5), c.IsDBNull(6) ? 0 : c.GetInt64(6), Sql.Dbl(c, 7), Sql.Dbl(c, 8), total, Sql.Str(c, 10),
            c.IsDBNull(11) ? null : c.GetInt64(11), Sql.Str(c, 12), Sql.Str(c, 13),
            c.IsDBNull(14) ? total : c.GetDouble(14), false, Sql.Str(c, 15));
    }

    private static double BarrelsOf(SwingResult r, int meter) => r.Parts.FirstOrDefault(p => p.Meter == meter)?.Barrels ?? 0.0;

    /// <summary>
    /// يسجّل الزفة مرة واحدة فقط: إن وُجد سجل يتداخل زمنيًا (≥ نصف المدة الأقصر) ويشترك معه في العدّاد فهي العملية نفسها
    /// (إعادة إرسال/إعادة اتصال/نقر مزدوج) فيُرجَع السجل الموجود بـ Duplicate=true. إن مُرِّر truck تُحفظ كمية الوايت
    /// المدخلة كقيمة للزفة (زفة كاملة) وتبقى الكمية المقاسة في Measured. مع sessionId: سجل واحد لكل جلسة يُحدَّث.
    /// </summary>
    public SwingRecord Insert(SwingResult r, long startMs, long endMs, Truck? truck = null, string? photo = null, string? sessionId = null)
    {
        lock (_lock)
        {
            string meters = string.Join("+", r.Parts.Select(p => (p.Meter + 1).ToString(CultureInfo.InvariantCulture)));
            if (sessionId != null)
            {
                var ex = Sql.One(_db, $"SELECT {Cols} FROM swings WHERE session_id=@s", Read, ("@s", sessionId));
                if (ex != null) return UpdateSession(ex, r, meters, startMs, endMs, truck, photo);
            }
            else
            {
                var dup = FindDuplicate(meters, startMs, endMs);
                if (dup != null) return dup with { Duplicate = true };
            }

            string day = DayOf(endMs);
            int seq = Sql.Query(_db, "SELECT COUNT(*) FROM swings WHERE day=@d", x => x.GetInt32(0), ("@d", day))[0] + 1;
            double m1 = BarrelsOf(r, 0), m2 = BarrelsOf(r, 1);
            bool full = truck != null || r.Type == SwingType.Full;
            double total = truck?.Capacity ?? r.TotalBarrels;
            long id = Sql.InsertId(_db,
                "INSERT INTO swings(day,seq,full,meters,start_ms,end_ms,m1,m2,total,notes,measured,photo,truck_id,truck_name,session_id) " +
                "VALUES(@day,@seq,@full,@meters,@s,@e,@m1,@m2,@total,@notes,@measured,@photo,@tid,@tname,@sid)",
                ("@day", day), ("@seq", seq), ("@full", full ? 1 : 0), ("@meters", meters), ("@s", startMs), ("@e", endMs),
                ("@m1", m1), ("@m2", m2), ("@total", total), ("@notes", r.Notes), ("@measured", r.TotalBarrels),
                ("@photo", photo), ("@tid", truck?.Id), ("@tname", truck?.Name), ("@sid", sessionId));
            return new SwingRecord(id, day, seq, full, meters, startMs, endMs, m1, m2, total, r.Notes,
                truck?.Id, truck?.Name ?? "", photo ?? "", r.TotalBarrels, false, sessionId ?? "");
        }
    }

    private SwingRecord UpdateSession(SwingRecord ex, SwingResult r, string meters, long startMs, long endMs, Truck? truck, string? photo)
    {
        double m1 = BarrelsOf(r, 0), m2 = BarrelsOf(r, 1);
        var sets = new List<string> { "meters=@meters", "start_ms=@s", "end_ms=@e", "m1=@m1", "m2=@m2", "measured=@measured", "notes=@notes" };
        var args = new List<(string, object?)>
        {
            ("@meters", meters), ("@s", startMs), ("@e", endMs), ("@m1", m1), ("@m2", m2),
            ("@measured", r.TotalBarrels), ("@notes", r.Notes), ("@id", ex.Id),
        };
        if (truck != null)
        {
            sets.AddRange(new[] { "truck_id=@tid", "truck_name=@tname", "total=@total", "full=1" });
            args.AddRange(new (string, object?)[] { ("@tid", truck.Id), ("@tname", truck.Name), ("@total", truck.Capacity) });
        }
        else if (ex.TruckId != null) { /* رُبط بوايت سابقًا: تبقى كمية الوايت المدخلة */ }
        else
        {
            sets.AddRange(new[] { "total=@total", "full=@full" });
            args.AddRange(new (string, object?)[] { ("@total", r.TotalBarrels), ("@full", r.Type == SwingType.Full ? 1 : 0) });
        }
        if (!string.IsNullOrEmpty(photo)) { sets.Add("photo=@photo"); args.Add(("@photo", photo)); }
        Sql.Exec(_db, $"UPDATE swings SET {string.Join(",", sets)} WHERE id=@id", args.ToArray());
        return ByIdUnlocked(ex.Id) ?? ex;
    }

    private SwingRecord? FindDuplicate(string meters, long startMs, long endMs)
    {
        var mine = meters.Split('+').ToHashSet();
        var rows = Sql.Query(_db, $"SELECT {Cols} FROM swings WHERE start_ms < @e AND end_ms > @s", Read, ("@e", endMs), ("@s", startMs));
        foreach (var o in rows)
        {
            if (!o.Meters.Split('+').Any(mine.Contains)) continue;
            long overlap = Math.Min(endMs, o.EndMs) - Math.Max(startMs, o.StartMs);
            long shorter = Math.Max(Math.Min(endMs - startMs, o.EndMs - o.StartMs), 1);
            if (overlap * 2 >= shorter) return o;
        }
        return null;
    }

    /// <summary>يربط سجلًا بوايت (أو يصحّح الربط): الكمية = كمية الوايت المدخلة، زفة كاملة. آمنة عند التكرار.</summary>
    public void AssignTruck(long recordId, Truck t, string? photo)
    {
        lock (_lock)
        {
            // measured يحفظ القيمة المقاسة قبل أول استبدال
            Sql.Exec(_db, "UPDATE swings SET measured=COALESCE(measured,total) WHERE id=@id", ("@id", recordId));
            var set = "truck_id=@tid, truck_name=@tn, total=@cap, full=1" + (string.IsNullOrEmpty(photo) ? "" : ", photo=@photo");
            var args = new List<(string, object?)> { ("@tid", t.Id), ("@tn", t.Name), ("@cap", t.Capacity), ("@id", recordId) };
            if (!string.IsNullOrEmpty(photo)) args.Add(("@photo", photo));
            Sql.Exec(_db, $"UPDATE swings SET {set} WHERE id=@id", args.ToArray());
        }
    }

    public SwingRecord? ById(long id) { lock (_lock) return ByIdUnlocked(id); }
    private SwingRecord? ByIdUnlocked(long id) => Sql.One(_db, $"SELECT {Cols} FROM swings WHERE id=@id", Read, ("@id", id));

    /// <summary>عند إعادة تسمية وايت تتحدّث الأسماء في سجلاته (الكميات القديمة لا تتغيّر).</summary>
    public void RenameTruck(long truckId, string name) { lock (_lock) Sql.Exec(_db, "UPDATE swings SET truck_name=@n WHERE truck_id=@t", ("@n", name), ("@t", truckId)); }

    /// <summary>عند حذف وايت يبقى اسمه في السجلات القديمة لكن يُفكّ ارتباطه.</summary>
    public void DetachTruck(long truckId) { lock (_lock) Sql.Exec(_db, "UPDATE swings SET truck_id=NULL WHERE truck_id=@t", ("@t", truckId)); }

    public IReadOnlyList<SwingRecord> Query(string? day)
    {
        lock (_lock)
        {
            return day == null
                ? Sql.Query(_db, $"SELECT {Cols} FROM swings ORDER BY id DESC", Read)
                : Sql.Query(_db, $"SELECT {Cols} FROM swings WHERE day=@d ORDER BY id DESC", Read, ("@d", day));
        }
    }

    public double DayTotal(string day)
    {
        lock (_lock) return Sql.Query(_db, "SELECT COALESCE(SUM(total),0) FROM swings WHERE day=@d", r => r.GetDouble(0), ("@d", day))[0];
    }

    public void Clear() { lock (_lock) Sql.Exec(_db, "DELETE FROM swings"); }

    /// <summary>مسارات صور السجلات (لتنظيف الصور القديمة غير المستخدمة).</summary>
    public ISet<string> PhotoPaths()
    {
        lock (_lock) return Sql.Query(_db, "SELECT photo FROM swings WHERE photo IS NOT NULL AND photo<>''", r => r.GetString(0)).ToHashSet();
    }

    /// <summary>CSV بترميز UTF-8 مع BOM ليفتح العربي صحيحًا في Excel. الأعمدة القديمة بترتيبها والجديدة في النهاية.</summary>
    public void ExportCsv(Stream output)
    {
        using var w = new StreamWriter(output, new UTF8Encoding(false), 4096, leaveOpen: true);
        w.Write('\uFEFF');
        w.Write("رقم الزفة (يومي),التاريخ,نوع الزفة,العدّادات,وقت البدء,وقت الانتهاء,المدة (ث),براميل العداد 1,براميل العداد 2,إجمالي البراميل,ملاحظات,اسم الوايت,الكمية المقاسة (براميل)\n");
        static string Q(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
        static string F(double v) => v.ToString("0.000", CultureInfo.InvariantCulture);
        foreach (var r in Query(null).Reverse())
        {
            w.Write(string.Join(",", new[]
            {
                r.Seq.ToString(CultureInfo.InvariantCulture), r.Day, Q(r.TypeAr), r.Meters,
                TimeOf(r.StartMs), TimeOf(r.EndMs), ((r.EndMs - r.StartMs) / 1000).ToString(CultureInfo.InvariantCulture),
                F(r.M1Barrels), F(r.M2Barrels), F(r.Total), Q(r.Notes), Q(r.TruckName), F(r.Measured),
            }) + "\n");
        }
        w.Flush();
    }

    public void Dispose() => _db.Dispose();
}
