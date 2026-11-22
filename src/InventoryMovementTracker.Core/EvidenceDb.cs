using Microsoft.Data.Sqlite;

namespace InventoryMovementTracker.Core;

internal static class EvidenceDb
{
    public static List<T> Query<T>(string path, string sql, Func<SqliteDataReader, T> map)
    {
        // Pooling off so the file is released on dispose; the old database is evidence, never written.
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        var rows = new List<T>();
        while (r.Read()) rows.Add(map(r));
        return rows;
    }

    public static string? Text(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
}
