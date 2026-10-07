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

/// <param name="Tag">What it was about (<see cref="EventTags"/>); null in lines logged before tags were kept.</param>
public sealed record EventRow(DateTime AtUtc, GameId Game, string Level, string Message, string? Tag = null);

/// <summary>What a line of the activity log is about, as the save manager's Log tab tags it (MGR-09).</summary>
public static class EventTags
{
    public const string Upload = "upload";
    public const string Download = "download";

    /// <summary>A save added to the history only: a game its store's cloud syncs, or one kept before an update.</summary>
    public const string Backup = "backup";

    public const string Restore = "restore";
    public const string Named = "named";
    public const string Conflict = "conflict";

    /// <summary>Changed while the game wasn't running, and held for the person (BAK-11).</summary>
    public const string Held = "held";

    public const string InUse = "in use";
    public const string Session = "session";
    public const string Playing = "playing";
    public const string Daily = "daily";

    /// <summary>This PC already had the cloud's save: only the record of what was agreed moved on.</summary>
    public const string Sync = "sync";

    /// <summary>A drive or save folder that isn't there, saves gone from this PC, or none found yet.</summary>
    public const string Missing = "missing";

    /// <summary>The cloud's side waits: this PC is offline, or the cloud isn't connected yet (PC-05).</summary>
    public const string Offline = "offline";

    public const string Cloud = "cloud";
    public const string Blocked = "blocked";
    public const string Thinned = "thinned";
    public const string Error = "error";

    /// <summary>Saves packed into a shared zip, or a shared zip's saves imported (SHARE-01, SHARE-10).</summary>
    public const string Share = "share";

    /// <summary>The tags of lines where a save moved: the Log tab's Uploads, downloads and restores.</summary>
    public static IReadOnlySet<string> Moves { get; } = new HashSet<string>(StringComparer.Ordinal) { Upload, Download, Backup, Restore, Named, Share };
}

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
        AddColumn("events", "tag", "TEXT NULL");
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

    /// <param name="tag">What it's about, one of <see cref="EventTags"/>.</param>
    public void Log(GameId game, string level, string message, string? tag = null) =>
        Execute("INSERT INTO events (at_utc, game, level, message, tag) VALUES ($t, $g, $l, $m, $tag)",
            ("$t", Now()), ("$g", game.Value), ("$l", level), ("$m", message), ("$tag", tag));

    /// <summary>The newest lines first; every game's when <paramref name="game"/> is null. The log is kept for good.</summary>
    public IReadOnlyList<EventRow> GetEvents(GameId? game, int limit)
    {
        using var cmd = game is { } g
            ? Command("SELECT at_utc, game, level, message, tag FROM events WHERE game = $g ORDER BY id DESC LIMIT $n", ("$g", g.Value), ("$n", limit))
            : Command("SELECT at_utc, game, level, message, tag FROM events ORDER BY id DESC LIMIT $n", ("$n", limit));
        using var reader = cmd.ExecuteReader();
        var events = new List<EventRow>();
        while (reader.Read())
        {
            events.Add(new EventRow(ParseTime(reader.GetString(0)), GameId.Parse(reader.GetString(1)), reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
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

    /// <summary>
    /// A column newer than the table, added to a state.db made before it. Another process opening the same file may add it
    /// first; an older GameSync reading the file never names it, so it keeps working.
    /// </summary>
    private void AddColumn(string table, string column, string type)
    {
        using (var cmd = Command($"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = $c", ("$c", column)))
        {
            if (Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) > 0)
            {
                return;
            }
        }

        try
        {
            Execute($"ALTER TABLE {table} ADD COLUMN {column} {type}");
        }
        catch (SqliteException e) when (e.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
        {
            // Added by the other process in the meantime.
        }
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
