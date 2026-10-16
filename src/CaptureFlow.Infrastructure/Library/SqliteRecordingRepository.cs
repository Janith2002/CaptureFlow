using CaptureFlow.Core.Library;
using Microsoft.Data.Sqlite;

namespace CaptureFlow.Infrastructure.Library;

public sealed class SqliteRecordingRepository(string databasePath) : IRecordingRepository
{
    private const string Columns = "id,path,folder,type,format,duration,bytes,created,modified,missing,warning";
    private Task<T> Run<T>(Func<SqliteConnection, T> action, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false, DefaultTimeout = 5 }.ToString());
        connection.Open();
        using var schema = connection.CreateCommand();
        schema.CommandText = """
            CREATE TABLE IF NOT EXISTS library_recordings(
              id TEXT PRIMARY KEY,path TEXT NOT NULL UNIQUE COLLATE NOCASE,folder TEXT NOT NULL COLLATE NOCASE,
              type INTEGER NOT NULL,format TEXT NOT NULL,duration INTEGER,bytes INTEGER NOT NULL,
              created INTEGER NOT NULL,modified INTEGER NOT NULL,missing INTEGER NOT NULL,warning TEXT);
            CREATE INDEX IF NOT EXISTS library_folder ON library_recordings(folder);
            CREATE TABLE IF NOT EXISTS library_renames(id TEXT PRIMARY KEY,oldpath TEXT NOT NULL,newpath TEXT NOT NULL);
            """;
        schema.ExecuteNonQuery(); token.ThrowIfCancellationRequested();
        return action(connection);
    }, token);
    public async Task<IReadOnlyList<RecordingEntry>> GetFolderAsync(string folder, CancellationToken token) => await Run(connection =>
    {
        using var command = connection.CreateCommand(); command.CommandText = $"SELECT {Columns} FROM library_recordings WHERE folder=$folder";
        command.Parameters.AddWithValue("$folder", folder); using var reader = command.ExecuteReader(); var entries = new List<RecordingEntry>();
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            entries.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), (RecordingType)reader.GetInt32(3), reader.GetString(4),
                reader.IsDBNull(5) ? null : TimeSpan.FromTicks(reader.GetInt64(5)), reader.GetInt64(6), new(reader.GetInt64(7), TimeSpan.Zero), new(reader.GetInt64(8), TimeSpan.Zero), reader.GetBoolean(9), reader.IsDBNull(10) ? null : reader.GetString(10)));
        }
        return (IReadOnlyList<RecordingEntry>)entries;
    }, token).ConfigureAwait(false);
    public Task SaveAsync(IReadOnlyList<RecordingEntry> entries, CancellationToken token) => Run(connection =>
    {
        using var transaction = connection.BeginTransaction();
        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested(); using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = $"INSERT INTO library_recordings({Columns}) VALUES($id,$path,$folder,$type,$format,$duration,$bytes,$created,$modified,$missing,$warning) ON CONFLICT(path) DO UPDATE SET folder=excluded.folder,type=excluded.type,format=excluded.format,duration=excluded.duration,bytes=excluded.bytes,created=excluded.created,modified=excluded.modified,missing=excluded.missing,warning=excluded.warning";
            Bind(command, entry); command.ExecuteNonQuery();
        }
        token.ThrowIfCancellationRequested(); transaction.Commit(); return true;
    }, token);
    private static void Bind(SqliteCommand command, RecordingEntry entry)
    {
        command.Parameters.AddWithValue("$id", entry.Id.ToString()); command.Parameters.AddWithValue("$path", entry.FullPath);
        command.Parameters.AddWithValue("$folder", entry.Folder); command.Parameters.AddWithValue("$type", (int)entry.Type);
        command.Parameters.AddWithValue("$format", entry.Format); command.Parameters.AddWithValue("$duration", (object?)entry.Duration?.Ticks ?? DBNull.Value);
        command.Parameters.AddWithValue("$bytes", entry.Bytes); command.Parameters.AddWithValue("$created", entry.Created.UtcTicks);
        command.Parameters.AddWithValue("$modified", entry.Modified.UtcTicks); command.Parameters.AddWithValue("$missing", entry.IsMissing);
        command.Parameters.AddWithValue("$warning", (object?)entry.Warning ?? DBNull.Value);
    }
    public Task BeginRenameAsync(PendingRename rename, CancellationToken token) => Run(connection =>
    {
        using var command = connection.CreateCommand(); command.CommandText = "INSERT INTO library_renames(id,oldpath,newpath) VALUES($id,$old,$new)";
        command.Parameters.AddWithValue("$id", rename.Id.ToString()); command.Parameters.AddWithValue("$old", rename.OldPath); command.Parameters.AddWithValue("$new", rename.NewPath); command.ExecuteNonQuery(); return true;
    }, token);
    public Task CompleteRenameAsync(RecordingEntry entry, CancellationToken token) => Run(connection =>
    {
        using var transaction = connection.BeginTransaction(); using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "UPDATE library_recordings SET path=$path WHERE id=$id";
        command.Parameters.AddWithValue("$path", entry.FullPath); command.Parameters.AddWithValue("$id", entry.Id.ToString());
        if (command.ExecuteNonQuery() != 1) throw new IOException("Recording metadata disappeared during rename.");
        command.CommandText = "DELETE FROM library_renames WHERE id=$id"; command.ExecuteNonQuery(); transaction.Commit(); return true;
    }, token);
    public Task CancelRenameAsync(Guid id, CancellationToken token) => Run(connection => { Delete(connection, "library_renames", id); return true; }, token);
    public Task RemoveAsync(Guid id, CancellationToken token) => Run(connection =>
    {
        using var transaction = connection.BeginTransaction(); Delete(connection, "library_recordings", id, transaction); Delete(connection, "library_renames", id, transaction); transaction.Commit(); return true;
    }, token);
    private static void Delete(SqliteConnection connection, string table, Guid id, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"DELETE FROM {table} WHERE id=$id"; command.Parameters.AddWithValue("$id", id.ToString()); command.ExecuteNonQuery();
    }
    public async Task<IReadOnlyList<PendingRename>> GetRenamesAsync(CancellationToken token) => await Run(connection =>
    {
        using var command = connection.CreateCommand(); command.CommandText = "SELECT id,oldpath,newpath FROM library_renames"; using var reader = command.ExecuteReader(); var result = new List<PendingRename>();
        while (reader.Read()) { token.ThrowIfCancellationRequested(); result.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2))); }
        return (IReadOnlyList<PendingRename>)result;
    }, token).ConfigureAwait(false);
}
