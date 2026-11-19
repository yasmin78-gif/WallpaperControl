using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace WallpaperControl;

internal sealed record PriceWatchEntry(PriceWatchGame Game, PriceWatchReply<PriceWatchPrices[]>? Snapshot, int? TargetCents = null);
internal sealed class PriceWatchLimitException : Exception;

internal sealed partial class PriceWatchStore(string path)
{
    internal const int MaximumGames = 20;
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
        command.CommandText = """
            SELECT w.id, w.title, w.kind, w.snapshot, t.cents FROM watched w
            LEFT JOIN target_prices t ON t.live = w.live AND t.id = w.id
            WHERE w.live = $mode ORDER BY w.rowid
            """;
        command.Parameters.AddWithValue("$mode", live);
        using var reader = command.ExecuteReader();
        var entries = new List<PriceWatchEntry>();
        while (reader.Read())
        {
            var game = new PriceWatchGame(Guid.Parse(reader.GetString(0)), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2));
            var snapshot = reader.IsDBNull(3) ? null :
                JsonSerializer.Deserialize<PriceWatchReply<PriceWatchPrices[]>>(reader.GetString(3));
            entries.Add(new(game, snapshot, reader.IsDBNull(4) ? null : reader.GetInt32(4)));
        }
        return entries.ToArray();
    }

    internal bool Add(bool live, PriceWatchGame game)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*), COALESCE(SUM(id = $id), 0) FROM watched WHERE live = $mode";
        BindKey(command, live, game.Id);
        bool duplicate;
        long count;
        using (var reader = command.ExecuteReader())
        {
            reader.Read();
            count = reader.GetInt64(0);
            duplicate = reader.GetInt64(1) != 0;
        }
        if (duplicate) return false;
        if (count >= MaximumGames) throw new PriceWatchLimitException();
        bool inserted = Insert(connection, transaction, live, game);
        transaction.Commit();
        return inserted;
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

    internal bool SaveTarget(bool live, Guid id, int? cents)
    {
        if (cents < 0) throw new ArgumentOutOfRangeException(nameof(cents));
        using var connection = Open();
        using var command = connection.CreateCommand();
        BindKey(command, live, id);
        command.CommandText = cents == null
            ? "DELETE FROM target_prices WHERE live = $mode AND id = $id"
            : """
                INSERT INTO target_prices (live, id, cents)
                SELECT live, id, $cents FROM watched WHERE live = $mode AND id = $id
                ON CONFLICT (live, id) DO UPDATE SET cents = excluded.cents
                """;
        if (cents != null) command.Parameters.AddWithValue("$cents", cents.Value);
        return command.ExecuteNonQuery() != 0;
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

    internal PriceWatchReply<PriceWatchGameInfo>? ReadInfo(bool live, Guid id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM game_info WHERE live = $mode AND id = $id";
        BindKey(command, live, id);
        return command.ExecuteScalar() is string payload
            ? JsonSerializer.Deserialize<PriceWatchReply<PriceWatchGameInfo>>(payload) : null;
    }

    internal bool SaveInfo(bool live, Guid id, PriceWatchReply<PriceWatchGameInfo> info)
    {
        if (info.Data.Id != id) return false;
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO game_info (live, id, payload, fetched_ticks)
            SELECT live, id, $payload, $fetched FROM watched WHERE live = $mode AND id = $id
            ON CONFLICT (live, id) DO UPDATE SET payload = excluded.payload, fetched_ticks = excluded.fetched_ticks
            WHERE game_info.fetched_ticks <= excluded.fetched_ticks
            """;
        BindKey(command, live, id);
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(info));
        command.Parameters.AddWithValue("$fetched", info.FetchedAt.UtcTicks);
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
            DataSource = path, Pooling = false, DefaultTimeout = 3, ForeignKeys = true
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
                    CREATE TABLE IF NOT EXISTS game_info (
                        live INTEGER NOT NULL, id TEXT NOT NULL, payload TEXT NOT NULL, fetched_ticks INTEGER NOT NULL,
                        PRIMARY KEY (live, id), FOREIGN KEY (live, id) REFERENCES watched (live, id) ON DELETE CASCADE
                    );
                    CREATE TABLE IF NOT EXISTS migration (name TEXT PRIMARY KEY);
                    CREATE TABLE IF NOT EXISTS target_prices (
                        live INTEGER NOT NULL, id TEXT NOT NULL, cents INTEGER NOT NULL CHECK (cents >= 0),
                        PRIMARY KEY (live, id), FOREIGN KEY (live, id) REFERENCES watched (live, id) ON DELETE CASCADE
                    );
                    CREATE TABLE IF NOT EXISTS price_alerts (
                        live INTEGER NOT NULL, id TEXT NOT NULL, reached INTEGER NOT NULL CHECK (reached IN (0,1)),
                        PRIMARY KEY (live,id), FOREIGN KEY (live,id) REFERENCES target_prices(live,id) ON DELETE CASCADE
                    );
                    CREATE TABLE IF NOT EXISTS price_monitor_state (
                        live INTEGER PRIMARY KEY CHECK (live IN (0,1)), last_attempt INTEGER, last_success INTEGER
                    );
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
