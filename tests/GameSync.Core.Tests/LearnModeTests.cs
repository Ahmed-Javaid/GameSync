using GameSync.Core.Discovery;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Host;

namespace GameSync.Core.Tests;

/// <summary>
/// FIND-04, learn mode's watcher (Milestone 6, KAN-134): one session of a game nothing else found saves for is watched,
/// and the places it wrote to are offered; nothing syncs until the person picks one.
/// </summary>
public class LearnModeTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public void FIND_04_what_a_session_wrote_becomes_places_the_noise_left_out()
    {
        using var world = new TestWorld();
        string Folder(params string[] parts) => Directory.CreateDirectory(Path.Combine([world.Root, .. parts])).FullName;
        var localLow = Folder("Users", "Sam", "AppData", "LocalLow");
        var local = Folder("Users", "Sam", "AppData", "Local");
        var roaming = Folder("Users", "Sam", "AppData", "Roaming");
        var documents = Folder("Users", "Sam", "Documents");
        var install = Folder("Games", "Lantern Keep");
        var steam = Folder("Steam");
        var userdata = Folder("Steam", "userdata");
        var data = Folder("GameSync");
        var folders = new Dictionary<string, string>
        {
            ["<localLow>"] = localLow, ["<localAppData>"] = local, ["<roaming>"] = roaming, ["<documents>"] = documents,
            ["<steamRoot>"] = steam, ["<installDir>"] = install,
        };
        var scope = new LearnScope(
            [
                new LearnRoot(localLow, "<localLow>"), new LearnRoot(local, "<localAppData>"), new LearnRoot(roaming, "<roaming>"),
                new LearnRoot(documents, "<documents>"), new LearnRoot(userdata, "<steamRoot>/userdata", 2, LearnPlaces.SteamCloudTag),
                new LearnRoot(install, "<installDir>", 1, LearnPlaces.OwnFolderTag),
            ],
            folders,
            [data]);

        var start = DateTime.UtcNow.AddMinutes(-30);
        var end = DateTime.UtcNow.AddMinutes(-1);
        var written = new List<(string, DateTime)>();
        void Write(string path, string text = "a save", DateTime? when = null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            written.Add((path, when ?? start.AddMinutes(10)));
        }

        // Saves: two files under a company's folder share one game folder; a save list's slot in My Games; Shadlix's live
        // save deep in the game's own folder; a settings file loose in it; Steam's cloud folder for the game.
        Write(Path.Combine(localLow, "Lantern Works", "Lantern Keep", "Player.sav"));
        Write(Path.Combine(localLow, "Lantern Works", "Lantern Keep", "Settings.json"));
        Write(Path.Combine(documents, "My Games", "Lantern Keep", "Saves", "slot1.sav"));
        Write(Path.Combine(install, "user", "savedata", "1", "CUSA00207", "SPRJ0005", "userdata0000"));
        Write(Path.Combine(install, "user", "savedata", "1", "CUSA00207", "SPRJ0005", "userdata0001"));
        Write(Path.Combine(install, "settings.cfg"));
        Write(Path.Combine(userdata, "123456789", "440", "remote", "cfg.sav"));

        // Noise: logs, temporary files, caches, Windows' and Google's own, a program, Steam's own settings, GameSync's
        // own folder, and a save written before the game started.
        Write(Path.Combine(install, "Logs", "output.txt"));
        Write(Path.Combine(install, "player.log"));
        Write(Path.Combine(local, "Temp", "x.tmp"));
        Write(Path.Combine(local, "Lantern Keep", "ShaderCache", "shaders.bin"));
        Write(Path.Combine(roaming, "Microsoft", "Windows", "Recent", "a.dat"));
        Write(Path.Combine(local, "Google", "Chrome", "User Data", "Cookies"));
        Write(Path.Combine(install, "LanternKeep.exe"), "MZ a program");
        Write(Path.Combine(userdata, "123456789", "config", "localconfig.vdf"));
        Write(Path.Combine(data, "state.db"));
        Write(Path.Combine(roaming, "Old Game", "save.dat"), when: start.AddMinutes(-5));

        var finds = LearnPlaces.From(written, scope, start, end);
        // Names that look like saves first, then the most written, then the newest; Shadlix's numbered files don't look
        // like saves by their names, so they follow, the folder still narrowed to SPRJ0005.
        Assert.Equal(
        [
            ("<localLow>/Lantern Works/Lantern Keep", 2, false),
            ("<steamRoot>/userdata/123456789/440/remote", 1, false),
            ("<documents>/My Games/Lantern Keep/Saves", 1, false),
            ("<installDir>/user/savedata/1/CUSA00207/SPRJ0005", 2, false),
            ("<installDir>/settings.cfg", 1, true),
        ], finds.Places.Select(p => (p.Portable, p.Files, p.IsFile)));
        Assert.Equal([LearnPlaces.SaveLikeTag], finds.Places[0].Tags);
        Assert.Equal([LearnPlaces.SaveLikeTag, LearnPlaces.SteamCloudTag], finds.Places[1].Tags);
        Assert.Equal([LearnPlaces.OwnFolderTag], finds.Places[3].Tags);
        Assert.Equal([LearnPlaces.OwnFolderTag], finds.Places[4].Tags);
        Assert.Equal(["Player.sav", "Settings.json"], finds.Places[0].Examples);

        // Watch again: a later session's places join the earlier ones', a place seen again with its newer numbers.
        var later = new LearnFinds(end, end.AddMinutes(20), [finds.Places[0] with { Files = 5 }, new LearnPlace(Path.Combine(roaming, "Lantern Keep"),
            "<roaming>/Lantern Keep", false, 1, 10, end, ["world.dat"], [LearnPlaces.SaveLikeTag])]);
        var merged = LearnPlaces.Merge(finds, later);
        Assert.Equal(6, merged.Places.Count);
        Assert.Equal(("<localLow>/Lantern Works/Lantern Keep", 5), (merged.Places[0].Portable, merged.Places[0].Files));
        Assert.Equal((start, end.AddMinutes(20)), (merged.StartUtc, merged.EndUtc));
    }

    [Fact]
    public async Task FIND_04_the_recorder_hears_files_written_and_drops_the_noise_at_once()
    {
        using var world = new TestWorld();
        var folder = Directory.CreateDirectory(Path.Combine(world.Root, "Watched")).FullName;
        var scope = new LearnScope([new LearnRoot(folder, "<roaming>")], new Dictionary<string, string>(), []);
        using var recorder = new LearnRecorder([folder, Path.Combine(world.Root, "Not there")], path => LearnPlaces.SkipAtOnce(path, scope));
        Assert.Equal(1, recorder.Watching);

        var save = Path.Combine(folder, "Game", "slot1.sav");
        Directory.CreateDirectory(Path.GetDirectoryName(save)!);
        await File.WriteAllTextAsync(save, "a save", Ct);
        await File.WriteAllTextAsync(Path.Combine(folder, "Game", "game.log"), "noise", Ct);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && recorder.Written.All(w => !w.Path.EndsWith("slot1.sav", StringComparison.OrdinalIgnoreCase)))
        {
            await Task.Delay(100, Ct);
        }

        Assert.Contains(recorder.Written, w => w.Path.Equals(save, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(recorder.Written, w => w.Path.EndsWith(".log", StringComparison.OrdinalIgnoreCase));
        Assert.False(recorder.Overflowed);
    }

    [Fact]
    public async Task FIND_04_a_game_nothing_found_saves_for_is_watched_once_and_its_saves_are_proposed()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        new AppConfig { Remote = world.Cloud, Games = [] }.Save(data);
        using (var state = new StateStore(data))
        {
            state.GetOrCreateDevice("DESKTOP");
        }

        var install = Path.Combine(world.Root, "Games", "Lantern Keep");
        var exe = FakeGames.Install(install, "LanternKeep");
        var game = GameId.Parse("lantern-keep");
        using (var library = new LibraryStore(data))
        {
            library.SaveAll([new LibraryEntry { Id = game, Title = "Lantern Keep", Store = StoreKind.Loose, InstallDir = install }]);
        }

        // Before it's played: learn mode waits for it, as first run promised.
        using (var state = new StateStore(data))
        using (var library = new LibraryStore(data))
        {
            Assert.Equal(LearnState.Waiting, LearnMode.View(data, state, library.All().Single(), syncs: false).State);
        }

        var told = new List<string>();
        var save = Path.Combine(install, "Saves", "slot1.sav");
        using (var agent = new Agent(data, new Told(told)))
        {
            await agent.TickAsync(DateTime.UtcNow, Ct);
            using var running = FakeGames.Run(exe, "--save", save, "--write-at", "1", "--run", "3");
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline && !File.Exists(LearnMode.FindsFile(data, game)))
            {
                await agent.TickAsync(DateTime.UtcNow, Ct);
                await Task.Delay(500, Ct);
            }
        }

        var finds = LearnMode.ReadFinds(data, game);
        Assert.NotNull(finds);

        // Other programs can write in the folders watched while the game runs (on the owner's PC their own GameSync among
        // them), so the game's place is looked for rather than taken as the first: it looks like saves, so only places that
        // also look like saves may come before it.
        var places = finds.Places.ToList();
        var at = places.FindIndex(p => p.Portable == "<installDir>/Saves");
        Assert.True(at >= 0, "The game's own place is missing: " + string.Join("; ", places.Select(p => $"{p.Portable} ({p.Files})")));
        var place = places[at];
        Assert.Equal(1, place.Files);
        Assert.Contains(LearnPlaces.SaveLikeTag, place.Tags);
        Assert.All(places.Take(at), p => Assert.True(p.LooksLikeSaves, p.Portable));
        Assert.Contains(told, t => t.StartsWith("Lantern Keep: learn mode found where it saves", StringComparison.Ordinal));
        using (var state = new StateStore(data))
        using (var library = new LibraryStore(data))
        {
            var entry = library.All().Single();
            Assert.Equal(LearnState.Found, LearnMode.View(data, state, entry, syncs: false).State);

            // It isn't watched again while what it found waits for the person; Watch again asks for one more session.
            Assert.False(LearnMode.Wants(state, entry, syncs: false, found: true));
            LearnMode.Set(data, game, on: true);
            Assert.True(LearnMode.Wants(state, entry, syncs: false, found: true));
        }

        // Sync these saves: the place is added as Add a place adds one, the game syncs, and learn mode is done with it.
        await LearnMode.AddAsync(data, game, [place.Path], () => { }, Ct);
        Assert.False(File.Exists(LearnMode.FindsFile(data, game)));
        using (var engine = Engine.Open(data))
        {
            var synced = engine.Games.Single(g => g.Id == game);
            Assert.Contains(synced.Roots.Values, r => string.Equals(Path.GetFullPath(r), Path.GetFullPath(Path.Combine(install, "Saves")), StringComparison.OrdinalIgnoreCase));
            var entry = engine.Library.All().Single();
            Assert.Equal(LearnState.Off, LearnMode.View(data, engine.State, entry, syncs: true).State);
        }
    }

    [Fact]
    public void R15_a_game_with_an_anti_cheat_never_gets_learn_mode()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        Directory.CreateDirectory(data);
        using var state = new StateStore(data);
        var entry = new LibraryEntry { Id = GameId.Parse("apex-legends"), Title = "Apex Legends", AntiCheat = "EasyAntiCheat" };
        Assert.False(LearnMode.Wants(state, entry, syncs: false, found: false));
        state.SetSetting(LearnMode.Key(entry.Id), "on");
        Assert.False(LearnMode.Wants(state, entry, syncs: false, found: false));
        Assert.Equal(LearnState.AntiCheat, LearnMode.View(data, state, entry, syncs: false).State);

        // Turned off, an ordinary game isn't watched either; a game that syncs only when asked.
        var lantern = new LibraryEntry { Id = GameId.Parse("lantern-keep"), Title = "Lantern Keep" };
        state.SetSetting(LearnMode.Key(lantern.Id), "off");
        Assert.False(LearnMode.Wants(state, lantern, syncs: false, found: false));
        state.SetSetting(LearnMode.Key(lantern.Id), "");
        Assert.True(LearnMode.Wants(state, lantern, syncs: false, found: false));
        Assert.False(LearnMode.Wants(state, lantern, syncs: true, found: false));
        state.SetSetting(LearnMode.Key(lantern.Id), "on");
        Assert.True(LearnMode.Wants(state, lantern, syncs: true, found: false));
    }

    /// <summary>The agent's output, keeping what it tells the person.</summary>
    private sealed class Told(List<string> told) : IAgentOutput
    {
        public void Say(string line)
        {
        }

        public void NeedsYou(string title, string message) => told.Add($"{title}: {message}");

        public void Tell(string title, string message) => told.Add($"{title}: {message}");
    }
}
