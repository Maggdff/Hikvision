using System.Globalization;
using Microsoft.Data.Sqlite;
using SmartWater.Data;

namespace SmartWater.Ebs;

public sealed record EbsCustomer(long Id, string Name, string? Mobile, string? Address, long? AccountId);
public sealed record EbsBalance(long AccountId, double Amount, long CurrencyId);

public enum InvoiceStatus { LocalCreated, Syncing, MasterSuccess, DetailsPending, DetailsSuccess, Synced, SyncFailed, NeedsConfirm }

public sealed record InvoiceRow(
    string LocalId, long SwingId, string CustomerName, double Barrels, double Price, double Total, string Notes,
    long CreatedMs, string EbsNumericId, InvoiceStatus Status, long? MasterId, long? DetailsId,
    int Attempts, long? LastAttemptMs, string? LastError);

/// <summary>
/// قاعدة WaterGate/EBS المحلية (ebs.db): نسخة قراءة فقط من عملاء EBS وأرصدتهم وعملاته، + طابور الفواتير المعلّقة للمزامنة.
/// لا تمسّ swings.db ولا trucks.db. لا تحتفظ بأي حساب مالي: الأرصدة تُعرض كما جاءت من EBS.
/// </summary>
public sealed class EbsDb : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly object _lock = new();
    private int _seq;

    public EbsDb(string path)
    {
        _db = Sql.Open(path);
        if (Sql.UserVersion(_db) < 1)
        {
            Sql.Exec(_db, "CREATE TABLE IF NOT EXISTS customers(id INTEGER PRIMARY KEY, name TEXT NOT NULL, mobile TEXT, address TEXT, account_id INTEGER)");
            Sql.Exec(_db, "CREATE INDEX IF NOT EXISTS idx_customers_name ON customers(name COLLATE NOCASE)");
            Sql.Exec(_db, "CREATE TABLE IF NOT EXISTS customer_balances(account_id INTEGER NOT NULL, amount REAL NOT NULL, currency_id INTEGER NOT NULL, PRIMARY KEY(account_id,currency_id))");
            Sql.Exec(_db, "CREATE TABLE IF NOT EXISTS currencies(id INTEGER PRIMARY KEY, name TEXT NOT NULL)");
            Sql.Exec(_db, "CREATE TABLE IF NOT EXISTS invoices(local_id TEXT PRIMARY KEY, swing_id INTEGER NOT NULL UNIQUE, customer_name TEXT NOT NULL, barrels REAL NOT NULL, price REAL NOT NULL, total REAL NOT NULL, notes TEXT, created_ms INTEGER NOT NULL, ebs_numeric_id TEXT NOT NULL UNIQUE, status TEXT NOT NULL, master_id INTEGER, details_id INTEGER, attempts INTEGER NOT NULL DEFAULT 0, last_attempt_ms INTEGER, last_error TEXT)");
            Sql.SetUserVersion(_db, 1);
        }
    }

    public static string Norm(string? s) => System.Text.RegularExpressions.Regex.Replace((s ?? "").Trim(), @"\s+", " ");

    // ---------------- نسخة العملاء (تُستبدل بالكامل عند كل تحميل ناجح)
    public void ReplaceCustomers(IReadOnlyList<EbsCustomer> list)
    {
        lock (_lock)
        {
            Sql.Exec(_db, "BEGIN");
            try
            {
                Sql.Exec(_db, "DELETE FROM customers");
                foreach (var c in list)
                    Sql.Exec(_db, "INSERT OR REPLACE INTO customers(id,name,mobile,address,account_id) VALUES(@i,@n,@m,@a,@acc)",
                        ("@i", c.Id), ("@n", c.Name), ("@m", c.Mobile), ("@a", c.Address), ("@acc", c.AccountId));
                Sql.Exec(_db, "COMMIT");
            }
            catch { Sql.Exec(_db, "ROLLBACK"); throw; }
        }
    }

    public void ReplaceBalances(IReadOnlyList<EbsBalance> list)
    {
        lock (_lock)
        {
            Sql.Exec(_db, "BEGIN");
            try
            {
                Sql.Exec(_db, "DELETE FROM customer_balances");
                foreach (var b in list)
                    Sql.Exec(_db, "INSERT OR REPLACE INTO customer_balances(account_id,amount,currency_id) VALUES(@a,@m,@c)", ("@a", b.AccountId), ("@m", b.Amount), ("@c", b.CurrencyId));
                Sql.Exec(_db, "COMMIT");
            }
            catch { Sql.Exec(_db, "ROLLBACK"); throw; }
        }
    }

    public void ReplaceCurrencies(IReadOnlyList<(long Id, string Name)> list)
    {
        lock (_lock)
        {
            Sql.Exec(_db, "DELETE FROM currencies");
            foreach (var (id, n) in list) Sql.Exec(_db, "INSERT OR REPLACE INTO currencies(id,name) VALUES(@i,@n)", ("@i", id), ("@n", n));
        }
    }

    public IReadOnlyList<EbsCustomer> Customers()
    {
        lock (_lock)
            return Sql.Query(_db, "SELECT id,name,mobile,address,account_id FROM customers ORDER BY name COLLATE NOCASE",
                r => new EbsCustomer(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetInt64(4)));
    }

    public EbsCustomer? FindCustomer(string name)
    {
        var n = Norm(name).ToLowerInvariant();
        return Customers().FirstOrDefault(c => Norm(c.Name).ToLowerInvariant() == n);
    }

    /// <summary>أرصدة حساب بكل العملات (سالب = مدين، موجب = دائن كما في رد EBS).</summary>
    public IReadOnlyList<(string Currency, double Amount)> BalancesOf(long accountId)
    {
        lock (_lock)
            return Sql.Query(_db,
                "SELECT COALESCE(c.name, CAST(b.currency_id AS TEXT)), b.amount FROM customer_balances b LEFT JOIN currencies c ON c.id=b.currency_id WHERE b.account_id=@a",
                r => (r.GetString(0), r.GetDouble(1)), ("@a", accountId));
    }

    public int CustomerCount() { lock (_lock) return Sql.Query(_db, "SELECT COUNT(*) FROM customers", r => r.GetInt32(0))[0]; }

    // ---------------- طابور الفواتير
    private const string InvCols = "local_id,swing_id,customer_name,barrels,price,total,notes,created_ms,ebs_numeric_id,status,master_id,details_id,attempts,last_attempt_ms,last_error";

    private static InvoiceRow ReadInv(SqliteDataReader r) => new(
        r.GetString(0), r.GetInt64(1), r.GetString(2), r.GetDouble(3), r.GetDouble(4), r.GetDouble(5), r.IsDBNull(6) ? "" : r.GetString(6),
        r.GetInt64(7), r.GetString(8), Enum.Parse<InvoiceStatus>(r.GetString(9)), r.IsDBNull(10) ? null : r.GetInt64(10),
        r.IsDBNull(11) ? null : r.GetInt64(11), r.GetInt32(12), r.IsDBNull(13) ? null : r.GetInt64(13), r.IsDBNull(14) ? null : r.GetString(14));

    /// <summary>رقم صرف فريد (TheNumberFromApp): وقت + عدّاد، مع التأكد من عدم وجوده.</summary>
    private string NewNumericId(long nowMs)
    {
        var t = DateTimeOffset.FromUnixTimeMilliseconds(nowMs).ToLocalTime().ToString("yyMMddHHmmss", CultureInfo.InvariantCulture);
        while (true)
        {
            _seq = (_seq + 1) % 100;
            var id = t + _seq.ToString("00", CultureInfo.InvariantCulture);
            if (Sql.Query(_db, "SELECT 1 FROM invoices WHERE ebs_numeric_id=@i", r => 1, ("@i", id)).Count == 0) return id;
            nowMs += 1000;
            t = DateTimeOffset.FromUnixTimeMilliseconds(nowMs).ToLocalTime().ToString("yyMMddHHmmss", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>يضيف فاتورة للطابور مرة واحدة لكل زفة (swing_id فريد). null = موجودة مسبقًا.</summary>
    public InvoiceRow? AddInvoice(long swingId, string customer, double barrels, double price, string notes, long createdMs, long nowMs)
    {
        lock (_lock)
        {
            if (Sql.Query(_db, "SELECT 1 FROM invoices WHERE swing_id=@s", r => 1, ("@s", swingId)).Count > 0) return null;
            var row = new InvoiceRow(Guid.NewGuid().ToString(), swingId, customer, barrels, price, Math.Round(barrels * price, 4), notes, createdMs,
                NewNumericId(nowMs), InvoiceStatus.LocalCreated, null, null, 0, null, null);
            Sql.Exec(_db, $"INSERT INTO invoices({InvCols}) VALUES(@l,@s,@c,@b,@p,@t,@n,@cm,@e,@st,NULL,NULL,0,NULL,NULL)",
                ("@l", row.LocalId), ("@s", swingId), ("@c", customer), ("@b", barrels), ("@p", price), ("@t", row.Total), ("@n", notes),
                ("@cm", createdMs), ("@e", row.EbsNumericId), ("@st", row.Status.ToString()));
            return row;
        }
    }

    public InvoiceRow? Invoice(string localId)
    {
        lock (_lock) return Sql.One(_db, $"SELECT {InvCols} FROM invoices WHERE local_id=@l", ReadInv, ("@l", localId));
    }

    public InvoiceRow? InvoiceOfSwing(long swingId)
    {
        lock (_lock) return Sql.One(_db, $"SELECT {InvCols} FROM invoices WHERE swing_id=@s", ReadInv, ("@s", swingId));
    }

    public IReadOnlyList<InvoiceRow> Invoices(bool pendingOnly)
    {
        lock (_lock)
            return Sql.Query(_db, $"SELECT {InvCols} FROM invoices" + (pendingOnly ? " WHERE status<>'Synced'" : "") + " ORDER BY created_ms, local_id", ReadInv);
    }

    public void Update(InvoiceRow r)
    {
        lock (_lock)
            Sql.Exec(_db, "UPDATE invoices SET notes=@n, ebs_numeric_id=@e, status=@st, master_id=@m, details_id=@d, attempts=@a, last_attempt_ms=@la, last_error=@le WHERE local_id=@l",
                ("@n", r.Notes), ("@e", r.EbsNumericId), ("@st", r.Status.ToString()), ("@m", r.MasterId), ("@d", r.DetailsId),
                ("@a", r.Attempts), ("@la", r.LastAttemptMs), ("@le", r.LastError), ("@l", r.LocalId));
    }

    /// <summary>رقم صرف جديد (يُستعمل فقط عند تأكيد المستخدم لتحذير «سجل مكرر»).</summary>
    public string NextNumericId(long nowMs) { lock (_lock) return NewNumericId(nowMs); }

    public void Dispose() => _db.Dispose();
}
