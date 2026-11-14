using Microsoft.Data.Sqlite;

namespace SmartWater.Data;

/// <summary>مساعدات صغيرة فوق Microsoft.Data.Sqlite (معاملات مسمّاة، null → DBNull).</summary>
internal static class Sql
{
    public static SqliteConnection Open(string path)
    {
        if (path != ":memory:")
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }
        var c = new SqliteConnection(path == ":memory:" ? "Data Source=:memory:" : $"Data Source={path}");
        c.Open();
        return c;
    }

    private static SqliteCommand Cmd(SqliteConnection c, string sql, (string, object?)[] args)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        return cmd;
    }

    public static int Exec(SqliteConnection c, string sql, params (string, object?)[] args)
    {
        using var cmd = Cmd(c, sql, args);
        return cmd.ExecuteNonQuery();
    }

    public static long InsertId(SqliteConnection c, string sql, params (string, object?)[] args)
    {
        Exec(c, sql, args);
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT last_insert_rowid()";
        return (long)cmd.ExecuteScalar()!;
    }

    public static List<T> Query<T>(SqliteConnection c, string sql, Func<SqliteDataReader, T> map, params (string, object?)[] args)
    {
        using var cmd = Cmd(c, sql, args);
        using var r = cmd.ExecuteReader();
        var l = new List<T>();
        while (r.Read()) l.Add(map(r));
        return l;
    }

    public static T? One<T>(SqliteConnection c, string sql, Func<SqliteDataReader, T> map, params (string, object?)[] args) where T : class
        => Query(c, sql, map, args).FirstOrDefault();

    public static double Dbl(SqliteDataReader r, int i) => r.IsDBNull(i) ? 0.0 : r.GetDouble(i);
    public static string Str(SqliteDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetString(i);

    public static int UserVersion(SqliteConnection c)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA user_version";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public static void SetUserVersion(SqliteConnection c, int v) => Exec(c, $"PRAGMA user_version = {v}");
}
