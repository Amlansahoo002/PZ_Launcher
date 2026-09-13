using System.Data;
using System.Text;
using Microsoft.Data.Sqlite;
using PZ_ChunkWiper;

namespace PZLauncher.Desktop;

internal static class DatabaseReader
{
    internal static SqliteConnection Open(string path, bool readOnly = true)
    {
        if (!File.Exists(path)) throw new FileNotFoundException(T("db.missing"), path);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite, Pooling = false, DefaultTimeout = 3 }.ToString());
        connection.Open(); return connection;
    }
    internal static void Snapshot(string source, string destination)
    {
        using var input = Open(source);
        using var output = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Pooling = false }.ToString());
        output.Open(); input.BackupDatabase(output);
    }
    internal static string Quote(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";
    internal static List<string> Tables(string path)
    {
        using var connection = Open(path); using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_schema WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
        using var reader = command.ExecuteReader(); var tables = new List<string>(); while (reader.Read()) tables.Add(reader.GetString(0)); return tables;
    }
    internal static DataTable Read(string path, string table, int offset = 0)
    {
        if (!Tables(path).Contains(table) || offset < 0) throw new InvalidDataException(T("db.table"));
        using var connection = Open(path); using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM " + Quote(table) + " LIMIT 200 OFFSET $offset";
        command.Parameters.AddWithValue("$offset", offset);
        using var reader = command.ExecuteReader(); var data = new DataTable(table); data.Load(reader); return data;
    }
    internal static string Blob(string path, string table, string column, byte[] data, IReadOnlyDictionary<string,object> values)
    {
        if (data.Length > 16 * 1024 * 1024) return T("db.large", data.Length) + "\r\n" + Convert.ToHexString(data.AsSpan(0, Math.Min(256, data.Length)));
        string filename = Path.GetFileName(path);
        if (filename is not ("players.db" or "vehicles.db") || data.Length < 27)
            return T("db.raw", data.Length) + "\r\n" + Convert.ToHexString(data.AsSpan(0, Math.Min(512, data.Length)));
        try
        {
            string inspection = PzBlobInspector.Describe(filename, table, column, data, values, path);
            if (filename == "players.db") inspection = PzBlobInspector.DescribeCharacterSheet(filename, table, column, data, values, path) + "\r\n\r\n" + inspection;
            return T("db.parserNote") + "\r\n\r\n" + inspection;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { return T("db.decodeFailed") + "\r\n" + ex.Message + "\r\n" + Convert.ToHexString(data.AsSpan(0, Math.Min(512, data.Length))); }
    }
}
