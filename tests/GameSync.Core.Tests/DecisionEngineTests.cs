using System.Security.Cryptography;
using System.Text;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.Scanning;
using GameSync.Core.Sync;

namespace GameSync.Core.Tests;

/// <summary>The decision table and every guardrail, on the pure decision function.</summary>
public class DecisionEngineTests
{
    private static readonly GameId G = GameId.Parse("game");
    private static readonly DateTime T0 = new(2026, 9, 27, 18, 0, 0, DateTimeKind.Utc);
    private static readonly DeviceInfo Desktop = new(DeviceId.Parse("d-desktop"), "DESKTOP");
    private static readonly DeviceInfo Laptop = new(DeviceId.Parse("d-laptop"), "LAPTOP");
    private static readonly SessionInfo PlayedToday = new(T0, T0.AddHours(3));

    private static readonly GameDefinition Game = new()
    {
        Id = G,
        Title = "Game",
        Roots = new Dictionary<string, string> { ["saves"] = @"C:\Saves\Game" },
        Rules = [new SaveRule { Root = "saves" }],
    };

    private static readonly VersionRecord V1 = V("v1", null, Desktop, 0, F("a.sav", "one"));

    [Fact]
    public void SYNC_01_nothing_changed_does_nothing() =>
        Assert.Equal(SyncAction.None, Decide(Inputs([F("a.sav", "one")], V1, V1)).Action);

    [Fact]
    public void SYNC_01_change_made_during_play_uploads_on_top_of_the_cloud()
    {
        var decision = Decide(Inputs([F("a.sav", "two", 10)], V1, V1));

        Assert.Equal(SyncAction.Upload, decision.Action);
        Assert.Equal(VersionKind.Normal, decision.UploadKind);
        Assert.Equal(V1.Id, decision.UploadParent);
    }

    [Fact]
    public void SYNC_01_newer_cloud_save_downloads_without_keeping_anything()
    {
        var v2 = V("v2", V1, Laptop, 20, F("a.sav", "two", 15));

        var decision = Decide(Inputs([F("a.sav", "one")], V1, V1, v2));

        Assert.Equal(SyncAction.Download, decision.Action);
        Assert.Equal(v2.Id, decision.Head!.Id);
        Assert.Null(decision.KeepLocalFirst);
    }

    [Fact]
    public void SYNC_01_both_changed_the_same_way_just_agrees()
    {
        var v2 = V("v2", V1, Laptop, 20, F("a.sav", "two", 15));

        Assert.Equal(SyncAction.AdoptHead, Decide(Inputs([F("a.sav", "two", 15)], V1, V1, v2)).Action);
    }

    [Fact]
    public void SYNC_03_two_uploads_from_the_same_parent_need_you()
    {
        var v2 = V("v2", V1, Laptop, 20, F("a.sav", "laptop", 15));
        var v3 = V("v3", V1, Desktop, 21, F("a.sav", "desktop", 16));

        var decision = Decide(Inputs([F("a.sav", "desktop", 16)], v3, V1, v2, v3));

        Assert.Equal(SyncAction.NeedsYou, decision.Action);
        Assert.Contains("same starting point", decision.Reason);
    }

    [Fact]
    public void SYNC_04_this_pc_newer_uploads_and_pins_the_cloud_save()
    {
        var v2 = V("v2", V1, Laptop, 20, F("a.sav", "laptop", 10));

        var decision = Decide(Inputs([F("a.sav", "desktop", 30)], V1, V1, v2));

        Assert.Equal(SyncAction.Upload, decision.Action);
        Assert.Equal(v2.Id, decision.UploadParent);
        Assert.Equal(v2.Id, Assert.Single(decision.Pins).Version);
        Assert.Contains("Swap", decision.Notice);
    }

    [Fact]
    public void SYNC_04_cloud_newer_downloads_and_keeps_this_pcs_save_pinned()
    {
        var v2 = V("v2", V1, Laptop, 40, F("a.sav", "laptop", 30));

        var decision = Decide(Inputs([F("a.sav", "desktop", 10)], V1, V1, v2));

        Assert.Equal(SyncAction.Download, decision.Action);
        Assert.Equal(VersionOrigin.KeptInConflict, decision.KeepLocalFirst!.Origin);
    }

    [Fact]
    public void SYNC_05_first_sync_keeps_the_cloud_current_and_the_local_files_in_history()
    {
        var decision = Decide(Inputs([F("a.sav", "older local", 90)], null, V1));

        Assert.Equal(SyncAction.Download, decision.Action);
        Assert.Equal(VersionOrigin.KeptAtFirstSync, decision.KeepLocalFirst!.Origin);
    }

    [Fact]
    public void SYNC_05_first_sync_with_the_same_files_just_agrees() =>
        Assert.Equal(SyncAction.AdoptHead, Decide(Inputs([F("a.sav", "one")], null, V1)).Action);

    [Fact]
    public void SYNC_06_newer_side_that_lost_most_files_needs_you()
    {
        var base4 = V("v1", null, Desktop, 0, F("a.sav", "a"), F("b.sav", "b"), F("c.sav", "c"), F("d.sav", "d"));
        var cloud = V("v2", base4, Laptop, 20, F("a.sav", "a"), F("b.sav", "b"), F("c.sav", "c2", 10), F("d.sav", "d"));

        var decision = Decide(Inputs([F("a.sav", "a-new", 30)], base4, base4, cloud));

        Assert.Equal(SyncAction.NeedsYou, decision.Action);
        Assert.Contains("more than half", decision.Reason);
    }

    [Fact]
    public void SYNC_07_newer_side_changed_outside_play_needs_you()
    {
        var v2 = V("v2", V1, Laptop, 20, F("a.sav", "laptop", 10));

        var decision = Decide(Inputs([F("a.sav", "desktop", 30)], V1, V1, v2) with { Sessions = [] });

        Assert.Equal(SyncAction.NeedsYou, decision.Action);
        Assert.Contains("wasn't running", decision.Reason);
    }

    [Fact]
    public void SYNC_09_always_ask_waits_for_you()
    {
        var v2 = V("v2", V1, Laptop, 20, F("a.sav", "laptop", 10));

        var decision = Decide(Inputs([F("a.sav", "desktop", 30)], V1, V1, v2) with { Game = Game with { ConflictPolicy = ConflictPolicy.AlwaysAsk } });

        Assert.Equal(SyncAction.NeedsYou, decision.Action);
    }

    [Fact]
    public void SYNC_09_this_pc_always_wins_even_when_older()
    {
        var v2 = V("v2", V1, Laptop, 40, F("a.sav", "laptop", 30));

        var decision = Decide(Inputs([F("a.sav", "desktop", 10)], V1, V1, v2) with { Game = Game with { ConflictPolicy = ConflictPolicy.ThisPcWins } });

        Assert.Equal(SyncAction.Upload, decision.Action);
        Assert.Equal(v2.Id, Assert.Single(decision.Pins).Version);
    }

    [Fact]
    public void SYNC_11_a_waiting_conflict_stays_until_decided()
    {
        var v2 = V("v2", V1, Laptop, 20, F("a.sav", "laptop", 10));

        var decision = Decide(Inputs([F("a.sav", "desktop", 30)], V1, V1, v2) with { ConflictPending = true });

        Assert.Equal(SyncAction.NeedsYou, decision.Action);
    }

    [Fact]
    public void SYNC_12_missing_drive_is_not_available()
    {
        var decision = Decide(Inputs([], V1, V1) with { Problems = [new RootProblem("saves", @"G:\Game", "Drive G: is not connected", DriveMissing: true)] });

        Assert.Equal(SyncAction.Unavailable, decision.Action);
        Assert.Contains("Drive G:", decision.Reason);
    }

    [Fact]
    public void SYNC_12_missing_folder_that_held_saves_is_not_available()
    {
        var problem = new RootProblem("saves", @"C:\Saves\Game", @"Save folder not found: C:\Saves\Game", DriveMissing: false);

        Assert.Equal(SyncAction.Unavailable, Decide(Inputs([], V1, V1) with { Problems = [problem] }).Action);
    }

    [Fact]
    public void SYNC_12_missing_folder_on_a_new_pc_just_downloads()
    {
        var problem = new RootProblem("saves", @"C:\Saves\Game", @"Save folder not found: C:\Saves\Game", DriveMissing: false);

        Assert.Equal(SyncAction.Download, Decide(Inputs([], null, V1) with { Problems = [problem] }).Action);
    }

    [Fact]
    public void SYNC_13_deleting_some_files_during_play_syncs()
    {
        var two = V("v1", null, Desktop, 0, F("a.sav", "a"), F("b.sav", "b"));

        Assert.Equal(SyncAction.Upload, Decide(Inputs([F("a.sav", "a")], two, two)).Action);
    }

    [Fact]
    public void SYNC_13_losing_every_file_never_syncs() =>
        Assert.Equal(SyncAction.SavesMissing, Decide(Inputs([], V1, V1)).Action);

    [Fact]
    public void BAK_07_reinstall_follows_the_first_sync_rule()
    {
        var decision = Decide(Inputs([F("a.sav", "fresh new world", 30)], V1, V1) with { Reinstalled = true });

        Assert.Equal(SyncAction.Download, decision.Action);
        Assert.Equal(VersionOrigin.KeptAtFirstSync, decision.KeepLocalFirst!.Origin);
    }

    [Fact]
    public void BAK_11_change_outside_play_is_held()
    {
        var decision = Decide(Inputs([F("a.sav", "edited", 400)], V1, V1));

        Assert.Equal(SyncAction.UploadHeld, decision.Action);
        Assert.Equal(VersionKind.Held, decision.UploadKind);
        Assert.True(decision.Held);
    }

    [Fact]
    public void BAK_11_held_change_is_not_uploaded_twice()
    {
        var held = V("v2", V1, Desktop, 410, F("a.sav", "edited", 400)) with { Kind = VersionKind.Held, Origin = VersionOrigin.OutOfSession };

        var decision = Decide(Inputs([F("a.sav", "edited", 400)], V1, V1, held));

        Assert.Equal(SyncAction.None, decision.Action);
        Assert.True(decision.Held);
    }

    [Fact]
    public void BAK_11_approved_change_uploads()
    {
        var decision = Decide(Inputs([F("a.sav", "edited", 400)], V1, V1) with { TreatChangesAsInSession = true });

        Assert.Equal(SyncAction.Upload, decision.Action);
    }

    [Fact]
    public void Backup_only_games_back_up_and_never_download()
    {
        var backupOnly = Game with { Mode = GameMode.BackupOnly };
        var laptops = V("v2", V1, Laptop, 20, F("a.sav", "laptop", 10));

        Assert.Equal(SyncAction.None, Decide(Inputs([F("a.sav", "one")], V1, V1, laptops) with { Game = backupOnly }).Action);
        Assert.Equal(SyncAction.Upload, Decide(Inputs([F("a.sav", "mine", 30)], V1, V1, laptops) with { Game = backupOnly }).Action);
    }

    private static SyncDecision Decide(SyncInputs inputs) => DecisionEngine.Decide(inputs);

    private static SyncInputs Inputs(IReadOnlyList<FileEntry> local, VersionRecord? basis, params VersionRecord[] versions) => new()
    {
        Game = Game,
        Device = Desktop,
        Local = local,
        Base = basis,
        Versions = versions,
        Sessions = [PlayedToday],
    };

    private static FileEntry F(string name, string content, int minute = 0) =>
        new($"saves/{name}", content.Length, T0.AddMinutes(minute), BlobId.FromHash(SHA256.HashData(Encoding.UTF8.GetBytes(content))));

    private static VersionRecord V(string id, VersionRecord? parent, DeviceInfo device, int minute, params FileEntry[] files) => new()
    {
        Id = VersionId.Parse(id),
        Game = G,
        Parent = parent?.Id,
        Kind = VersionKind.Normal,
        Origin = VersionOrigin.Session,
        Device = device,
        CreatedUtc = T0.AddMinutes(minute),
        Files = files,
    };
}
