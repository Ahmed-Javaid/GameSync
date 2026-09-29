using System.Globalization;
using GameSync.Core.State;
using GameSync.Core.Storage;

namespace GameSync.Host;

/// <summary>What the tray icon shows (BG-07). When several apply, the first here wins: needs you, then offline, playing, working.</summary>
public enum TrayMood
{
    /// <summary>Every game that syncs is synced; no badge.</summary>
    Synced,

    /// <summary>A sync is running, or saves wait to upload and are being tried again.</summary>
    Working,

    /// <summary>A game is being played; it syncs once it closes.</summary>
    Playing,

    /// <summary>The cloud can't be reached; saves are kept on this PC until it can.</summary>
    Offline,

    /// <summary>Something waits for the person: a conflict, a review, saves not found, a blocked file, a sign-in, a full Drive.</summary>
    NeedsYou,
}

/// <summary>How this PC's games that sync are doing, counted for the tray icon.</summary>
public sealed record SyncCounts(int Syncing, int Synced, int NeedYou, int Waiting)
{
    public static SyncCounts None { get; } = new(0, 0, 0, 0);

    /// <summary>A status that waits for the person: a conflict, a review, a missing folder, a file in use, a blocked file.</summary>
    public static bool NeedsYou(GameStatus? status) =>
        status is GameStatus.Conflict or GameStatus.HeldForReview or GameStatus.FilesInUse or GameStatus.SavesMissing or GameStatus.Blocked or GameStatus.Error;

    public static SyncCounts Of(IEnumerable<GameStatus?> statuses)
    {
        var all = statuses.ToList();
        return new SyncCounts(
            all.Count,
            all.Count(s => s is GameStatus.Synced or GameStatus.BackupOnly),
            all.Count(NeedsYou),
            all.Count(s => s is GameStatus.UploadPending or GameStatus.NewerInCloud));
    }

    /// <summary>The games that sync on this PC, as this PC's state has them; none before GameSync is set up.</summary>
    public static SyncCounts Read(string dataDir)
    {
        if (!File.Exists(AppConfig.PathIn(dataDir)))
        {
            return None;
        }

        using var engine = Engine.Open(dataDir);
        return Of(engine.Games.Select(g => engine.State.GetState(g.Id).Status));
    }
}

/// <summary>BG-07: the tray icon's state, and what hovering over it says: the news, then the counts.</summary>
public sealed record TrayStatus(TrayMood Mood, string Tooltip)
{
    /// <param name="cloud">Why the cloud couldn't be used in the latest sync, if it couldn't.</param>
    /// <param name="playing">The game being played, if one is.</param>
    public static TrayStatus From(bool setUp, SyncCounts counts, bool working, CloudErrorKind? cloud, string? playing)
    {
        if (!setUp)
        {
            return new TrayStatus(TrayMood.Synced, "GameSync isn't set up on this PC yet");
        }

        var (mood, news) = (cloud, counts, playing, working) switch
        {
            (CloudErrorKind.SignInExpired, _, _, _) => (TrayMood.NeedsYou, "sign in to Google Drive again"),
            (CloudErrorKind.StorageFull, _, _, _) => (TrayMood.NeedsYou, "your Google Drive is full"),
            (_, { NeedYou: 1 }, _, _) => (TrayMood.NeedsYou, "1 game needs you"),
            (_, { NeedYou: > 1 }, _, _) => (TrayMood.NeedsYou, $"{Number(counts.NeedYou)} games need you"),
            (CloudErrorKind.Offline, _, _, _) => (TrayMood.Offline, "offline, saves wait on this PC"),
            (_, _, { } game, _) => (TrayMood.Playing, $"playing {game}"),
            // Skip for now in first run: saves are kept on this PC, as asked; nothing is wrong and nothing is being tried.
            (CloudErrorKind.NotConnected, _, _, false) => (TrayMood.Synced, "every version is kept on this PC; no cloud is connected yet"),
            (_, _, _, true) => (TrayMood.Working, "syncing"),
            (_, { Waiting: > 0 }, _, _) => (TrayMood.Working, "trying the upload again soon"),
            (_, { Syncing: 0 }, _, _) => (TrayMood.Synced, "no games sync yet"),
            _ => (TrayMood.Synced, "all synced"),
        };

        var parts = new List<string>();
        if (counts.Syncing > 0)
        {
            parts.Add($"{Number(counts.Synced)} of {Number(counts.Syncing)} synced");
        }

        if (counts.NeedYou > 0)
        {
            parts.Add(counts.NeedYou == 1 ? "1 needs you" : $"{Number(counts.NeedYou)} need you");
        }

        if (counts.Waiting > 0)
        {
            parts.Add($"{Number(counts.Waiting)} waiting");
        }

        return new TrayStatus(mood, parts.Count == 0 ? $"GameSync: {news}" : $"GameSync: {news}\n{string.Join(" · ", parts)}");
    }

    /// <summary>The cloud's trouble in a sync, the most pressing first; none when every game reached it.</summary>
    public static CloudErrorKind? CloudTrouble(IReadOnlyList<Core.Sync.GameResult> results)
    {
        var kinds = results.Select(r => r.CloudProblem).OfType<CloudErrorKind>().ToHashSet();
        foreach (var kind in new[] { CloudErrorKind.SignInExpired, CloudErrorKind.StorageFull, CloudErrorKind.Offline })
        {
            if (kinds.Contains(kind))
            {
                return kind;
            }
        }

        return kinds.Count > 0 ? kinds.First() : null;
    }

    private static string Number(int n) => n.ToString(CultureInfo.InvariantCulture);
}
