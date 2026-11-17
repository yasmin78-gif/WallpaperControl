using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace WallpaperControl;

internal sealed record PriceWatchEntry(PriceWatchGame Game, PriceWatchReply<PriceWatchPrices[]>? Snapshot);

internal sealed class PriceWatchStore(string path)
{
    internal static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WallpaperControl", "PriceWatch", "watchlist.sqlite");

    private bool initialized, migrated;

    internal void Migrate(PriceWatchGame? test, PriceWatchGame? live)
    {
        if (migrated) return;
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var marker = connection.CreateCommand();
        marker.Transaction = transaction;
        marker.CommandText = "INSERT OR IGNORE INTO migration (name) VALUES ('legacy_selection')";
        if (marker.ExecuteNonQuery() != 0)
        {
            if (test != null) Insert(connection, transaction, false, test);
            if (live != null) Insert(connection, transaction, true, live);
        }
        transaction.Commit();
        migrated = true;
    }

    internal PriceWatchEntry[] Read(bool live)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, title, kind, snapshot FROM watched WHERE live = $mode ORDER BY rowid";
        command.Parameters.AddWithValue("$mode", live);
        using var reader = command.ExecuteReader();
        var entries = new List<PriceWatchEntry>();
        while (reader.Read())
        {
            var game = new PriceWatchGame(Guid.Parse(reader.GetString(0)), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2));
            var snapshot = reader.IsDBNull(3) ? null :
                JsonSerializer.Deserialize<PriceWatchReply<PriceWatchPrices[]>>(reader.GetString(3));
            entries.Add(new(game, snapshot));
        }
        return entries.ToArray();
    }

    internal bool Add(bool live, PriceWatchGame game)
    {
        using var connection = Open();
        return Insert(connection, null, live, game);
    }

    private static bool Insert(SqliteConnection connection, SqliteTransaction? transaction, bool live, PriceWatchGame game)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO watched (live, id, title, kind) VALUES ($mode, $id, $title, $kind)
            ON CONFLICT (live, id) DO NOTHING
            """;
        BindKey(command, live, game.Id);
        command.Parameters.AddWithValue("$title", game.Title);
        command.Parameters.AddWithValue("$kind", (object?)game.Type ?? DBNull.Value);
        return command.ExecuteNonQuery() != 0;
    }

    internal void Remove(bool live, Guid id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM watched WHERE live = $mode AND id = $id";
        BindKey(command, live, id);
        command.ExecuteNonQuery();
    }

    internal bool SaveSnapshot(bool live, Guid id, PriceWatchReply<PriceWatchPrices[]> snapshot)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE watched SET snapshot = $snapshot, fetched_ticks = $fetched
            WHERE live = $mode AND id = $id AND (fetched_ticks IS NULL OR fetched_ticks <= $fetched)
            """;
        BindKey(command, live, id);
        command.Parameters.AddWithValue("$snapshot", JsonSerializer.Serialize(snapshot));
        command.Parameters.AddWithValue("$fetched", snapshot.FetchedAt.UtcTicks);
        return command.ExecuteNonQuery() != 0;
    }

    private static void BindKey(SqliteCommand command, bool live, Guid id)
    {
        command.Parameters.AddWithValue("$mode", live);
        command.Parameters.AddWithValue("$id", id.ToString("D"));
    }

    private SqliteConnection Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Pooling = false, DefaultTimeout = 3
        }.ToString());
        try
        {
            connection.Open();
            if (!initialized)
            {
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE IF NOT EXISTS watched (
                        live INTEGER NOT NULL CHECK (live IN (0, 1)),
                        id TEXT NOT NULL,
                        title TEXT NOT NULL,
                        kind TEXT,
                        snapshot TEXT,
                        fetched_ticks INTEGER,
                        PRIMARY KEY (live, id)
                    );
                    CREATE TABLE IF NOT EXISTS migration (name TEXT PRIMARY KEY);
                    """;
                command.ExecuteNonQuery();
                initialized = true;
            }
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }
}
