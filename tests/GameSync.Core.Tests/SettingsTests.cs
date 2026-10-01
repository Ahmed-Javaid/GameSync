using GameSync.Core.Discovery;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Core.Storage;
using GameSync.Host;
using GameSync.UI.Theming;
using GameSync.UI.ViewModels;

namespace GameSync.Core.Tests;

/// <summary>
/// Settings (SET-01 to SET-06): what new games start from and applying it to the games already syncing, moving the
/// backup folder, renaming this PC, the notification switches, Copy diagnostics, and the page's own checks.
/// </summary>
public class SettingsTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly GameId Lantern = GameId.Parse("lantern-keep");

    [Fact]
    public async Task SET_06_a_new_game_starts_from_the_defaults_and_a_change_applies_to_games_already_syncing()
    {
        using var world = new TestWorld();
        var data = NewGame(world);
        SettingsData.SetDefaults(data, new GameDefaults
        {
            SettingsFiles = GameDefaults.Off,
            Screenshots = true,
            SkipJunk = false,
            Skip = ["*.bak"],
            Conflict = ConflictPolicy.AlwaysAsk,
        });

        // Sync these saves: the game starts from the defaults.
        Assert.True(await AppActions.SyncGameAsync(data, Lantern, new Quiet(), Ct));
        var game = Confirmed(data);
        Assert.Equal((ConflictPolicy.AlwaysAsk, true, false), (game.ConflictPolicy, game.IncludeScreenshots, game.SyncConfig));
        var saves = game.Rules.Single(r => r.Category == SaveCategory.Save);
        var settings = game.Rules.Single(r => r.Category == SaveCategory.Config);
        Assert.Contains("**/*.bak", saves.Exclude);
        Assert.False(saves.UseDefaultExcludes);
        Assert.Contains("**", settings.Exclude);
        Assert.True(GameSettings.Takes(saves, "slot1.sav"));
        Assert.False(GameSettings.Takes(saves, "old/slot1.bak"));

        var view = SettingsData.Read(data);
        Assert.Equal((1, 0, 0), (view.Games, view.FilesDiffer, view.ConflictDiffer));

        // Changed defaults leave the game as it is until they're applied to it.
        SettingsData.SetDefaults(data, view.Defaults with { SettingsFiles = GameDefaults.ThisPc, Skip = ["*.bak", "**/Mods/**"], Conflict = ConflictPolicy.NewestWins });
        view = SettingsData.Read(data);
        Assert.Equal((1, 1), (view.FilesDiffer, view.ConflictDiffer));
        Assert.Equal(Text(game), Text(Confirmed(data)));

        Assert.StartsWith("1 game follows the defaults now", await SettingsData.ApplyDefaultsAsync(data, files: true, conflict: false, () => { }, Ct));
        game = Confirmed(data);
        saves = game.Rules.Single(r => r.Category == SaveCategory.Save);
        Assert.DoesNotContain("**", game.Rules.Single(r => r.Category == SaveCategory.Config).Exclude);
        Assert.False(GameSettings.Takes(saves, "Mods/Better Lights/slot1.sav"));
        Assert.Equal(ConflictPolicy.AlwaysAsk, game.ConflictPolicy);
        Assert.Equal((0, 1), (SettingsData.Read(data).FilesDiffer, SettingsData.Read(data).ConflictDiffer));

        await SettingsData.ApplyDefaultsAsync(data, files: false, conflict: true, () => { }, Ct);
        Assert.Equal(ConflictPolicy.NewestWins, Confirmed(data).ConflictPolicy);
        Assert.Equal("Every game follows the defaults already.", await SettingsData.ApplyDefaultsAsync(data, files: true, conflict: true, () => { }, Ct));
    }

    [Theory]
    [InlineData("**")]
    [InlineData("*")]
    [InlineData("**/*")]
    [InlineData("")]
    [InlineData("C:\\Saves\\*.sav")]
    [InlineData("../other/*.sav")]
    [InlineData("/*.sav")]
    public void SET_06_a_pattern_that_skips_every_file_or_names_a_place_is_refused(string pattern) => Assert.NotNull(GameDefaults.Refusal(pattern));

    [Fact]
    public void SET_06_a_pattern_without_a_folder_skips_that_name_in_any_folder()
    {
        Assert.Null(GameDefaults.Refusal("*.bak"));
        Assert.Null(GameDefaults.Refusal("**/Mods/**"));
        Assert.Equal("**/*.bak", GameDefaults.Exclude("*.bak"));
        Assert.Equal("Mods/**", GameDefaults.Exclude("\\Mods\\**"));
        var rule = new SaveRule { Root = "saves", Exclude = [GameDefaults.Exclude("*.bak")] };
        Assert.False(GameSettings.Takes(rule, "slot1.bak"));
        Assert.False(GameSettings.Takes(rule, "backups/slot1.bak"));
        Assert.True(GameSettings.Takes(rule, "slot1.sav"));

        // The defaults as they start: nothing changes for a game, as before Settings had them.
        var plain = new GameDefinition { Id = Lantern, Title = "Lantern Keep", Roots = new Dictionary<string, string> { ["saves"] = "C:\\x" }, Rules = [rule] };
        Assert.True(new GameDefaults().FollowsFiles(plain));
        var applied = Assert.Single(new GameDefaults().Apply(plain).Rules);
        Assert.Equal((rule.Include, rule.UseDefaultExcludes), (applied.Include, applied.UseDefaultExcludes));
        Assert.Equal(rule.Exclude, applied.Exclude);
    }

    [Fact]
    public async Task FOLD_03_the_backup_folder_moves_with_every_file_checked_and_says_how_far_it_is()
    {
        using var world = new TestWorld();
        var data = NewGame(world);
        var output = new Quiet();
        Assert.True(await AppActions.SyncGameAsync(data, Lantern, output, Ct));
        await AppActions.BackUpNowAsync(data, Lantern, output, Ct);
        var old = SettingsData.Read(data).BackupFolder;
        Assert.True(Directory.EnumerateFiles(old, "*", SearchOption.AllDirectories).Any());

        // Refused: inside the folder it's in now; and a folder that isn't empty gets GameSync's own folder inside it.
        await Assert.ThrowsAsync<UsageException>(() => SettingsData.MoveBackupFolderAsync(data, Path.Combine(old, "inside"), null, () => { }, Ct));
        var picked = Path.Combine(world.Root, "Backups");
        Directory.CreateDirectory(Path.Combine(picked, "Other things"));
        var target = SettingsData.BackupFolderFor(picked);
        Assert.Equal(Path.Combine(picked, "GameSync"), target);
        Directory.CreateDirectory(Path.Combine(world.Root, "Empty"));
        Assert.Equal(Path.Combine(world.Root, "Empty"), SettingsData.BackupFolderFor(Path.Combine(world.Root, "Empty")));

        var progress = new Collect<(int Done, int Total)>();
        Assert.StartsWith("Moved the backup folder to", await SettingsData.MoveBackupFolderAsync(data, target, progress, () => { }, Ct));
        var (done, total) = progress.Seen[^1];
        Assert.True(total > 0);
        Assert.Equal(total, done);
        Assert.False(Directory.Exists(old));
        Assert.Equal(target, SettingsData.Read(data).BackupFolder);
        Assert.NotEmpty((await GameDetails.ReadAsync(data, Lantern, Ct))!.Versions);
        var figures = await SettingsData.FiguresAsync(data, Ct);
        Assert.True(figures is { Versions: > 0, Bytes: > 0 });
    }

    [Fact]
    public async Task PC_02_this_PC_is_renamed_unless_the_name_is_empty_too_long_or_another_PC_s()
    {
        using var world = new TestWorld();
        var data = NewGame(world);
        await new LocalHistory(Path.Combine(data, "history")).SaveDevicesAsync(
            [new DeviceRecord(DeviceId.Parse("d-0123456789abcdef"), "LAPTOP", "1.0.0", DateTime.UtcNow.AddHours(-2))], Ct);

        Assert.Throws<UsageException>(() => SettingsData.RenamePc(data, "  "));
        Assert.Throws<UsageException>(() => SettingsData.RenamePc(data, new string('X', SettingsData.NameLength + 1)));
        Assert.Throws<UsageException>(() => SettingsData.RenamePc(data, "laptop"));
        Assert.StartsWith("This PC is GAMING-PC now.", SettingsData.RenamePc(data, " GAMING-PC "));

        var view = SettingsData.Read(data);
        Assert.Equal("GAMING-PC", view.ThisPc);
        Assert.Equal(new[] { ("GAMING-PC", true), ("LAPTOP", false) }, view.Pcs.Select(p => (p.Name, p.ThisPc)));
        Assert.Equal("1.0.0", view.Pcs[1].AppVersion);
    }

    [Fact]
    public void SET_02_a_test_copy_never_changes_what_Windows_starts()
    {
        using var world = new TestWorld();
        var data = NewGame(world);
        Assert.False(SettingsData.Read(data).UsualDataFolder);
        Assert.Throws<UsageException>(() => SettingsData.SetDaily(data, new TimeOnly(21, 0)));
        Assert.Throws<UsageException>(() => SettingsData.SetStartAtSignIn(data, false));
        Assert.StartsWith("The daily backup runs now", SettingsData.RunDailyNow(data));
        Assert.True(SettingsData.Read(data).DailyAsked);
    }

    [Fact]
    public void BG_05_BG_06_notifications_wait_for_a_fullscreen_game_unless_turned_off_and_the_daily_line_is_offered()
    {
        using var world = new TestWorld();
        var data = NewGame(world);
        var view = SettingsData.Read(data);
        Assert.Equal((true, false), (view.HoldWhileFullscreen, view.DailyNote));

        SettingsData.SetNotifications(data, hold: false, dailyNote: true);
        Assert.False(SettingsData.HoldsWhileFullscreen(data));
        Assert.True(SettingsData.Read(data).DailyNote);

        // With holding off, the daily line shows at once, and again the next day although it reads the same.
        var shown = new List<(string, string)>();
        var output = new AppOutput(data, (title, message) => shown.Add((title, message)));
        Assert.False(output.HoldWhileFullscreen);
        ((IAgentOutput)output).Tell("Daily backup", "Checked 12 games; nothing new to upload.");
        ((IAgentOutput)output).Tell("Daily backup", "Checked 12 games; nothing new to upload.");
        Assert.Equal(2, shown.Count);

        Assert.Equal("Checked 12 games; nothing new to upload.", Daily.Summary(new DailyRun(DateTime.UtcNow, 12, 0, 0)));
        Assert.Equal("Checked 1 game and uploaded 1 new save.", Daily.Summary(new DailyRun(DateTime.UtcNow, 1, 1, 0)));
        Assert.Equal("Checked 12 games and uploaded 3 new saves. 2 games need you: open GameSync to see them.", Daily.Summary(new DailyRun(DateTime.UtcNow, 12, 3, 2)));
    }

    [Fact]
    public async Task SET_05_diagnostics_hold_the_settings_games_and_logs_but_never_a_sign_in_token()
    {
        using var world = new TestWorld();
        var data = NewGame(world);
        Assert.True(await AppActions.SyncGameAsync(data, Lantern, new Quiet(), Ct));
        var logs = Directory.CreateDirectory(Path.Combine(data, "logs")).FullName;
        File.WriteAllText(Path.Combine(logs, $"agent-{DateTime.Now:yyyy-MM-dd}.log"),
            "21:02:10  ! Drive: token ya29.a0AfH6SMBxExampleToken was refused; refresh_token=1//0gExampleRefreshToken12345 kept\n");

        var text = await SettingsData.DiagnosticsAsync(data, Ct);
        Assert.StartsWith("GameSync diagnostics, ", text);
        Assert.Contains("Lantern Keep", text);
        Assert.Contains("[sign-in token removed]", text);
        Assert.DoesNotContain("ya29.", text);
        Assert.DoesNotContain("ExampleRefreshToken", text);
    }

    [Fact]
    public void FOLD_08_a_folder_of_numbered_folders_is_taken_by_Steam_app_ID()
    {
        using var world = new TestWorld();
        var byId = Path.Combine(world.Root, "userdata");
        foreach (var app in new[] { "413150", "1145360", "504230", "config" })
        {
            Directory.CreateDirectory(Path.Combine(byId, app));
        }

        var plain = Path.Combine(world.Root, "Saves");
        foreach (var game in new[] { "Terraria", "Hades", "2064" })
        {
            Directory.CreateDirectory(Path.Combine(plain, game));
        }

        Assert.True(SettingsData.NamedById(byId));
        Assert.False(SettingsData.NamedById(plain));
        Assert.False(SettingsData.NamedById(world.Root + Path.DirectorySeparatorChar + "cloud"));
    }

    [Fact]
    public void LOOK_07_LOOK_10_appearance_saves_each_change_and_a_preset_brings_its_own_colours()
    {
        var saved = new List<Look>();
        var appearance = new AppearanceSettings(new SettingsActions { SetLook = saved.Add }, new Look());
        Assert.Equal(("cyan", "steel"), (appearance.PrimaryPick, appearance.SecondaryPick));

        appearance.PrimaryPick = "pink";
        Assert.Equal("pink", saved[^1].Primary);
        appearance.PrimaryPick = "cyan";
        Assert.Null(saved[^1].Primary);

        appearance.PrimaryPick = "lime";
        appearance.Preset = "tidal";
        Assert.Equal(new Look(Preset: "tidal"), saved[^1]);
        Assert.Equal(("blue", "aqua"), (appearance.PrimaryPick, appearance.SecondaryPick));

        appearance.Surface = Look.Solid;
        appearance.PureBlack = true;
        appearance.Mode = Look.Light;
        Assert.False(appearance.CanPureBlack);
        Assert.Contains("light mode stays Solid", appearance.SurfaceDescription);

        appearance.ResetCommand.Execute(null);
        Assert.Equal(new Look(Surface: Look.Solid), saved[^1]);
        Assert.Equal(7, appearance.Themes.Count);
    }

    [Fact]
    public async Task SET_02_the_daily_time_is_checked_before_it_changes()
    {
        var asked = new List<TimeOnly?>();
        var backup = new BackupSettings(new SettingsActions
        {
            SetDaily = at =>
            {
                asked.Add(at);
                return Task.FromResult(new Outcome("The daily backup runs at 07:30."));
            },
        }, () => { });
        Assert.Equal(new TimeOnly(20, 0), BackupSettings.Parse("20:00"));
        Assert.Equal(new TimeOnly(8, 5), BackupSettings.Parse(" 8:05 "));
        Assert.Equal(new TimeOnly(7, 30), BackupSettings.Parse("7.30"));
        Assert.Null(BackupSettings.Parse("25:00"));

        backup.EditTimeCommand.Execute(null);
        backup.TimeText = "half past seven";
        await backup.SaveTimeCommand.ExecuteAsync(null);
        Assert.True(backup.HasTimeError);
        Assert.Empty(asked);

        backup.TimeText = "7.30";
        await backup.SaveTimeCommand.ExecuteAsync(null);
        Assert.Equal([new TimeOnly(7, 30)], asked);
        Assert.Equal(("07:30", false), (backup.DailyAt, backup.EditingTime));
        Assert.Equal("The daily backup runs at 07:30.", backup.DailyNote?.Text);
    }

    /// <summary>A data folder whose library has Lantern Keep, not syncing yet, with a save folder and a settings file found.</summary>
    private static string NewGame(TestWorld world)
    {
        var data = Path.Combine(world.Root, "data");
        new AppConfig { Remote = world.Cloud, Games = [] }.Save(data);
        var saves = Path.Combine(world.Root, "Lantern Keep", "Saves");
        Write(Path.Combine(saves, "slot1.sav"), "at the lighthouse");
        Write(Path.Combine(saves, "slot1.bak"), "an older copy");
        Write(Path.Combine(saves, "Mods", "Better Lights", "slot1.sav"), "a mod's own save");
        var config = Path.Combine(world.Root, "Lantern Keep", "Config");
        Write(Path.Combine(config, "settings.ini"), "volume=7");
        var folders = Cli.FoldersForThisPc();
        using var library = new LibraryStore(data);
        library.SaveAll([new LibraryEntry
        {
            Id = Lantern,
            Title = "Lantern Keep",
            FirstSeenUtc = DateTime.UtcNow,
            Installed = true,
            Store = StoreKind.Loose,
            InstallDir = Path.Combine(world.Root, "Lantern Keep"),
            Proposals =
            [
                new Proposal(FoundBy.SaveList, RootResolver.ToPortable(saves, folders), "**", SaveCategory.Save, 3, 40, null),
                new Proposal(FoundBy.SaveList, RootResolver.ToPortable(config, folders), "settings.ini", SaveCategory.Config, 1, 8, null),
            ],
        }]);
        return data;
    }

    private static GameDefinition Confirmed(string data)
    {
        using var library = new LibraryStore(data);
        return library.All().Single(e => e.Id == Lantern).Confirmed!;
    }

    private static string Text(GameDefinition game) => System.Text.Json.JsonSerializer.Serialize(game, Json.Options);

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    /// <summary>Every report as it's made, on the thread that makes it (a test has no UI thread for <see cref="Progress{T}"/>).</summary>
    private sealed class Collect<T> : IProgress<T>
    {
        public List<T> Seen { get; } = [];

        public void Report(T value)
        {
            lock (Seen)
            {
                Seen.Add(value);
            }
        }
    }

    private sealed class Quiet : IAgentOutput
    {
        public void Say(string line)
        {
        }

        public void NeedsYou(string title, string message)
        {
        }
    }
}
