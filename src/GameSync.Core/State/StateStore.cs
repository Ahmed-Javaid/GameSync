using System.Globalization;
using System.Text.Json;
using GameSync.Core.Model;
using Microsoft.Data.Sqlite;

namespace GameSync.Core.State;

public enum GameStatus
{
    Synced,
    BackupOnly,
    Conflict,
    HeldForReview,
    FilesInUse,
    NotAvailable,
    SavesMissing,
    NoSaves,
    Blocked,
    Error,

    /// <summary>Saved in the backup folder on this PC; the upload waits (offline, Drive full, sign-in expired).</summary>
    UploadPending,

    /// <summary>The cloud has a newer save that couldn't be downloaded yet.</summary>
    NewerInCloud,

    /// <summary>A session is running; the game syncs once it closes.</summary>
    Playing,
}

public sealed record GameState(GameId Game, VersionRecord? Base, GameStatus? Status, string? Detail, bool Reinstalled, DateTime? UpdatedUtc);

public sealed record JobRow(long Id, string Kind, GameId Game, string State, int Attempts, string? LastError);

public sealed record EventRow(DateTime AtUtc, GameId Game, string Level, string Message);

/// <summary>This PC's own state, in <c>state.db</c>: never synced, and safe to rebuild from the cloud.</summary>
public sealed class StateStore : IDisposable
{
    private readonly SqliteConnection _db;

    public StateStore(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        _db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(dataDir, "state.db"), Pooling = false }.ToString());
        _db.Open();
        Execute("""
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = FULL;
            CREATE TABLE IF NOT EXISTS settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS sync_state (
                game TEXT PRIMARY KEY, base TEXT NULL, status TEXT NULL, detail TEXT NULL,
                reinstalled INTEGER NOT NULL DEFAULT 0, updated_utc TEXT NULL);
            CREATE TABLE IF NOT EXISTS sessions (id INTEGER PRIMARY KEY, game TEXT NOT NULL, start_utc TEXT NOT NULL, end_utc TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS jobs (
                id INTEGER PRIMARY KEY, kind TEXT NOT NULL, game TEXT NOT NULL, state TEXT NOT NULL,
                attempts INTEGER NOT NULL DEFAULT 0, last_error TEXT NULL, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS events (id INTEGER PRIMARY KEY, at_utc TEXT NOT NULL, game TEXT NOT NULL, level TEXT NOT NULL, message TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS hash_cache (path TEXT PRIMARY KEY, size INTEGER NOT NULL, mtime INTEGER NOT NULL, hash TEXT NOT NULL);
            """);
    }

    public void Dispose() => _db.Dispose();

    // ---- device ----

    public DeviceInfo GetOrCreateDevice(string defaultName)
    {
        var id = GetSetting("device.id");
        var name = GetSetting("device.name");
        if (id is not null && name is not null)
        {
            return new DeviceInfo(DeviceId.Parse(id), name);
        }

        var device = new DeviceInfo(DeviceId.New(), defaultName);
        SetSetting("device.id", device.Id.Value);
        SetSetting("device.name", device.Name);
        return device;
    }

    public void RenameDevice(string name) => SetSetting("device.name", name);

    public string? GetSetting(string key) =>
        Scalar<string>("SELECT value FROM settings WHERE key = $k", ("$k", key));

    public void SetSetting(string key, string value) =>
        Execute("INSERT INTO settings (key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = $v", ("$k", key), ("$v", value));

    /// <summary>Every setting whose key starts with <paramref name="prefix"/> and whose value isn't empty.</summary>
    public IReadOnlyDictionary<string, string> GetSettings(string prefix)
    {
        using var cmd = Command("SELECT key, value FROM settings WHERE substr(key, 1, length($p)) = $p AND value <> ''", ("$p", prefix));
        using var reader = cmd.ExecuteReader();
        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            settings[reader.GetString(0)] = reader.GetString(1);
        }

        return settings;
    }

    // ---- per-game sync state ----

    public GameState GetState(GameId game)
    {
        using var cmd = Command("SELECT base, status, detail, reinstalled, updated_utc FROM sync_state WHERE game = $g", ("$g", game.Value));
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            return new GameState(game, null, null, null, false, null);
        }

        var baseRecord = reader.IsDBNull(0) ? null : JsonSerializer.Deserialize<VersionRecord>(reader.GetString(0), Json.Options);
        GameStatus? status = reader.IsDBNull(1) ? null : Enum.Parse<GameStatus>(reader.GetString(1));
        return new GameState(
            game,
            baseRecord,
            status,
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetInt64(3) != 0,
            reader.IsDBNull(4) ? null : ParseTime(reader.GetString(4)));
    }

    /// <summary>The version this PC last agreed on. The whole record is kept, so the base survives thinning in the cloud.</summary>
    public void SetBase(GameId game, VersionRecord version)
    {
        EnsureRow(game);
        Execute("UPDATE sync_state SET base = $b, reinstalled = 0, updated_utc = $t WHERE game = $g",
            ("$g", game.Value), ("$b", JsonSerializer.Serialize(version, Json.Options)), ("$t", Now()));
    }

    public void SetStatus(GameId game, GameStatus status, string? detail)
    {
        EnsureRow(game);
        Execute("UPDATE sync_state SET status = $s, detail = $d, updated_utc = $t WHERE game = $g",
            ("$g", game.Value), ("$s", status.ToString()), ("$d", detail), ("$t", Now()));
    }

    /// <summary>BAK-07: the next sync of this game is treated as a first sync.</summary>
    public void MarkReinstalled(GameId game)
    {
        EnsureRow(game);
        Execute("UPDATE sync_state SET reinstalled = 1 WHERE game = $g", ("$g", game.Value));
    }

    // ---- play sessions (passed in until the process watcher exists) ----

    public void AddSession(GameId game, SessionInfo session) =>
        Execute("INSERT INTO sessions (game, start_utc, end_utc) VALUES ($g, $s, $e)",
            ("$g", game.Value), ("$s", Format(session.StartUtc)), ("$e", Format(session.EndUtc)));

    public IReadOnlyList<SessionInfo> GetSessions(GameId game)
    {
        using var cmd = Command("SELECT start_utc, end_utc FROM sessions WHERE game = $g ORDER BY start_utc", ("$g", game.Value));
        using var reader = cmd.ExecuteReader();
        var sessions = new List<SessionInfo>();
        while (reader.Read())
        {
            sessions.Add(new SessionInfo(ParseTime(reader.GetString(0)), ParseTime(reader.GetString(1))));
        }

        return sessions;
    }

    // ---- durable job queue (BAK-13) ----

    public long StartJob(string kind, GameId game)
    {
        Execute("INSERT INTO jobs (kind, game, state, attempts, created_utc, updated_utc) VALUES ($k, $g, 'running', 1, $t, $t)",
            ("$k", kind), ("$g", game.Value), ("$t", Now()));
        return Scalar<long>("SELECT last_insert_rowid()");
    }

    public void FinishJob(long id) =>
        Execute("UPDATE jobs SET state = 'done', last_error = NULL, updated_utc = $t WHERE id = $id", ("$id", id), ("$t", Now()));

    public void FailJob(long id, string error) =>
        Execute("UPDATE jobs SET state = 'failed', last_error = $e, updated_utc = $t WHERE id = $id", ("$id", id), ("$e", error), ("$t", Now()));

    /// <summary>Jobs a crash or reboot interrupted (still 'running'), plus failed ones worth retrying.</summary>
    public IReadOnlyList<JobRow> GetUnfinishedJobs()
    {
        using var cmd = Command("SELECT id, kind, game, state, attempts, last_error FROM jobs WHERE state IN ('running', 'failed') ORDER BY id");
        using var reader = cmd.ExecuteReader();
        var jobs = new List<JobRow>();
        while (reader.Read())
        {
            jobs.Add(new JobRow(reader.GetInt64(0), reader.GetString(1), GameId.Parse(reader.GetString(2)), reader.GetString(3),
                reader.GetInt32(4), reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return jobs;
    }

    /// <summary>Folds a resumed job into the new run that redoes its work.</summary>
    public void SupersedeJob(long id) =>
        Execute("UPDATE jobs SET state = 'superseded', updated_utc = $t WHERE id = $id", ("$id", id), ("$t", Now()));

    // ---- activity log ----

    public void Log(GameId game, string level, string message) =>
        Execute("INSERT INTO events (at_utc, game, level, message) VALUES ($t, $g, $l, $m)",
            ("$t", Now()), ("$g", game.Value), ("$l", level), ("$m", message));

    public IReadOnlyList<EventRow> GetEvents(GameId? game, int limit)
    {
        using var cmd = game is { } g
            ? Command("SELECT at_utc, game, level, message FROM events WHERE game = $g ORDER BY id DESC LIMIT $n", ("$g", g.Value), ("$n", limit))
            : Command("SELECT at_utc, game, level, message FROM events ORDER BY id DESC LIMIT $n", ("$n", limit));
        using var reader = cmd.ExecuteReader();
        var events = new List<EventRow>();
        while (reader.Read())
        {
            events.Add(new EventRow(ParseTime(reader.GetString(0)), GameId.Parse(reader.GetString(1)), reader.GetString(2), reader.GetString(3)));
        }

        return events;
    }

    // ---- hash cache: skip re-hashing files whose size and modified time haven't changed ----

    public BlobId? TryGetCachedHash(string fullPath, long size, long mtimeTicks)
    {
        var hash = Scalar<string>("SELECT hash FROM hash_cache WHERE path = $p AND size = $s AND mtime = $m",
            ("$p", fullPath.ToUpperInvariant()), ("$s", size), ("$m", mtimeTicks));
        return hash is null ? null : BlobId.Parse(hash);
    }

    public void CacheHash(string fullPath, long size, long mtimeTicks, BlobId hash) =>
        Execute("INSERT INTO hash_cache (path, size, mtime, hash) VALUES ($p, $s, $m, $h) ON CONFLICT(path) DO UPDATE SET size = $s, mtime = $m, hash = $h",
            ("$p", fullPath.ToUpperInvariant()), ("$s", size), ("$m", mtimeTicks), ("$h", hash.Value));

    // ---- plumbing ----

    private void EnsureRow(GameId game) =>
        Execute("INSERT OR IGNORE INTO sync_state (game) VALUES ($g)", ("$g", game.Value));

    private SqliteCommand Command(string sql, params (string Name, object? Value)[] parameters)
    {
        var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return cmd;
    }

    private void Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        using var cmd = Command(sql, parameters);
        cmd.ExecuteNonQuery();
    }

    private T? Scalar<T>(string sql, params (string Name, object? Value)[] parameters)
    {
        using var cmd = Command(sql, parameters);
        var result = cmd.ExecuteScalar();
        return result is null or DBNull ? default : (T)Convert.ChangeType(result, typeof(T), CultureInfo.InvariantCulture);
    }

    private static string Now() => Format(DateTime.UtcNow);

    private static string Format(DateTime utc) => utc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTime ParseTime(string text) =>
        DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
}
