using System.Diagnostics;
using System.IO.Compression;
using System.IO.Pipes;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using GameSync.Core.Discovery;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.Safety;
using GameSync.Core.State;
using GameSync.Core.Storage;
using GameSync.Core.Sync;
using GameSync.Host;
using GameSync.Storage.Drive;
using GameSync.UI.ViewModels;

namespace GameSync.Core.Tests;

/// <summary>
/// The safety audit (Milestone 6, R1 to R21; design system version 51): a test named after each rule where SafetyTests and
/// the feature tests don't already hold one, and the holes the audit found, closed.
/// </summary>
public class SafetyAuditTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly GameId Lantern = GameId.Parse("lantern-keep");

    [Fact]
    public async Task R2_a_program_in_a_save_folder_shows_under_its_place_and_is_logged_once()
    {
        using var world = new TestWorld();
        var (data, saves) = await SyncedGame(world);

        // Found at a backup: never backed up (R1), and the game says so, under the place it's in.
        File.WriteAllBytes(Path.Combine(saves, "Uninstall.exe"), SafetyTests.FakeProgram());
        await AppActions.BackUpNowAsync(data, Lantern, new Quiet(), Ct);
        var detail = (await GameDetails.ReadAsync(data, Lantern, Ct))!;
        var place = Assert.Single(detail.Places);
        Assert.Equal([Path.Combine(saves, "Uninstall.exe")], place.Programs);
        var logged = Assert.Single(detail.Log, l => l.Message.StartsWith("Left out Uninstall.exe", StringComparison.Ordinal));
        Assert.Equal(("warn", $"Left out Uninstall.exe: a program, never backed up or synced. It's in {saves}."), (logged.Level, logged.Message));

        // The saves page puts it under the place, in words.
        var item = new PlaceItem(place.Portable, place.Folder, place.Tag, place.Evidence, null) { Programs = place.Programs };
        Assert.Equal("A program is in this folder and isn't backed up: Uninstall.exe. Programs never travel with saves; if you didn't put it there, check where it came from.",
            item.ProgramNote);

        // Said once, not at every backup; a program renamed as a save is caught by its header (R1) and said too.
        File.WriteAllText(Path.Combine(saves, "slot1.sav"), "a later run");
        await AppActions.BackUpNowAsync(data, Lantern, new Quiet(), Ct);
        File.WriteAllBytes(Path.Combine(saves, "slot9.sav"), SafetyTests.FakeProgram());
        await AppActions.BackUpNowAsync(data, Lantern, new Quiet(), Ct);
        detail = (await GameDetails.ReadAsync(data, Lantern, Ct))!;
        Assert.Single(detail.Log, l => l.Message.StartsWith("Left out Uninstall.exe", StringComparison.Ordinal));
        Assert.Single(detail.Log, l => l.Message.StartsWith("Left out slot9.sav", StringComparison.Ordinal));
        item = new PlaceItem(place.Portable, place.Folder, place.Tag, place.Evidence, null) { Programs = Assert.Single(detail.Places).Programs };
        Assert.StartsWith("2 programs are in this folder and aren't backed up: ", item.ProgramNote);

        // Gone from the folder, gone from the page.
        File.Delete(Path.Combine(saves, "Uninstall.exe"));
        File.Delete(Path.Combine(saves, "slot9.sav"));
        File.WriteAllText(Path.Combine(saves, "slot1.sav"), "later still");
        await AppActions.BackUpNowAsync(data, Lantern, new Quiet(), Ct);
        Assert.Empty(Assert.Single((await GameDetails.ReadAsync(data, Lantern, Ct))!.Places).Programs);
    }

    [Theory]
    [InlineData(".exe")]
    [InlineData(".dll")]
    [InlineData(".sys")]
    [InlineData(".scr")]
    [InlineData(".com")]
    [InlineData(".bat")]
    [InlineData(".cmd")]
    [InlineData(".ps1")]
    [InlineData(".vbs")]
    [InlineData(".js")]
    [InlineData(".jse")]
    [InlineData(".wsf")]
    [InlineData(".hta")]
    [InlineData(".msi")]
    [InlineData(".lnk")]
    [InlineData(".url")]
    [InlineData(".reg")]
    [InlineData(".cpl")]
    [InlineData(".jar")]
    public void R1_every_program_extension_the_design_names_is_blocked(string extension) =>
        Assert.True(ProgramFileDetector.HasBlockedExtension("slot1" + extension.ToUpperInvariant()));

    [Fact]
    public async Task R1_a_program_hidden_under_a_saves_name_in_a_cloud_version_is_never_restored()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        desktop.AddGame("game");
        laptop.AddGame("game");
        desktop.Write("game", "slot.sav", "real");
        await desktop.SyncAsync();
        await laptop.SyncAsync();
        var real = Assert.Single(await Cloud.VersionsAsync(world, "game"));

        // A newer version whose slot.sav holds a program: its contents are in the cloud as any file's are, by their hash.
        var program = SafetyTests.FakeProgram();
        var hash = BlobId.FromHash(SHA256.HashData(program));
        await new FolderBlobStore(world.Cloud).PutAsync(GameId.Parse("game"), hash, new MemoryStream(program), Ct);
        var crafted = real with
        {
            Id = VersionId.Parse("evil-program"),
            Parent = real.Id,
            Device = new DeviceInfo(DeviceId.Parse("d-evil"), "EVIL"),
            CreatedUtc = DateTime.UtcNow,
            Files = [real.Files[0] with { Hash = hash, Size = program.Length, ModifiedUtc = DateTime.UtcNow }],
        };
        await File.WriteAllBytesAsync(Path.Combine(Cloud.VersionsFolder(world.Cloud, "game"), "evil-program.json"), JsonSerializer.SerializeToUtf8Bytes(crafted, Json.Options));

        // Its first bytes give it away as it's staged: nothing is put in place.
        var result = Assert.Single(await laptop.SyncAsync());
        Assert.Equal(GameStatus.Blocked, result.Status);
        Assert.Equal("'saves/slot.sav' is a program, not a save. Nothing was restored.", result.Message);
        Assert.Equal("real", laptop.Read("game", "slot.sav"));
    }

    [Fact]
    public void R3_Windows_antivirus_check_answers_for_a_plain_save()
    {
        using var world = new TestWorld();
        var save = Path.Combine(world.Root, "slot1.sav");
        File.WriteAllText(save, "at the lighthouse");
        using var amsi = new GameSync.Windows.AmsiScanner();
        Assert.NotEqual(ScanVerdict.Detected, amsi.ScanFile(save));
    }

    [Fact]
    public async Task R3_a_restore_no_antivirus_answered_for_says_so_on_the_game()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        desktop.AddGame("game");
        laptop.AddGame("game");
        desktop.Write("game", "slot.sav", "real");
        await desktop.SyncAsync();

        // No antivirus running on the laptop: the save still comes down, and its log says it came without that check.
        laptop.Malware.Answers = false;
        await laptop.SyncAsync();
        Assert.Equal("real", laptop.Read("game", "slot.sav"));
        Assert.Contains(laptop.State.GetEvents(GameId.Parse("game"), 50),
            e => e is { Level: "warn", Message: "No antivirus answered for 1 file of this restore, so it was put in place without that check." });
    }

    [Fact]
    public void R4_Drive_is_asked_for_a_file_without_ever_acknowledging_abuse()
    {
        // Google refuses a file it flags as malware unless the one asking says it knows; GameSync never says so.
        using var drive = new GoogleDriveClient(new NoCredential());
        Assert.False(drive.DownloadRequest("a-file").AcknowledgeAbuse);
        Assert.DoesNotContain(SourceFiles(), file => Regex.IsMatch(File.ReadAllText(file), @"AcknowledgeAbuse\s*=\s*true"));
    }

    [Fact]
    public void R5_every_kind_of_protected_folder_is_refused_and_a_save_folder_isnt()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        var guard = SensitivePathGuard.ForThisPc(data);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var folder in new[]
        {
            Path.Combine(profile, ".ssh"),
            Path.Combine(profile, ".aws"),
            Path.Combine(local, "Google", "Chrome", "User Data", "Default"),
            Path.Combine(roaming, "Mozilla", "Firefox", "Profiles"),
            Path.Combine(roaming, "Exodus"),
            Path.Combine(roaming, "Microsoft", "Credentials"),
            Path.Combine(roaming, "Microsoft", "Protect"),
            Path.Combine(local, "Microsoft", "Vault"),
            data,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32"),
            profile,
            Path.GetPathRoot(data)!,
        })
        {
            Assert.NotNull(guard.CheckRoot(folder));
        }

        var saves = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Lantern Keep");
        Assert.Null(guard.CheckRoot(saves));
        Assert.True(guard.IsBlockedFile(Path.Combine(profile, ".ssh", "id_ed25519")));
        Assert.False(guard.IsBlockedFile(Path.Combine(saves, "slot1.sav")));
    }

    [Fact]
    public void R5_a_junction_into_a_protected_folder_is_refused_where_it_really_leads()
    {
        using var world = new TestWorld();
        var secret = Directory.CreateDirectory(Path.Combine(world.Root, "keys")).FullName;
        var guard = new SensitivePathGuard([(secret, "it holds passwords or keys")]);
        var link = Path.Combine(world.Root, "Games", "Innocent Saves");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        Junction(link, secret);

        Assert.Equal(Path.TrimEndingDirectorySeparator(secret), Path.TrimEndingDirectorySeparator(SensitivePathGuard.RealPath(link)!), ignoreCase: true);
        Assert.Equal($"{link} can't hold saves (it leads to {SensitivePathGuard.RealPath(link)}): it holds passwords or keys", guard.CheckRoot(link));
        Assert.NotNull(guard.CheckRoot(Path.Combine(link, "Slot 1")));
        Assert.Null(guard.CheckRoot(Path.Combine(world.Root, "Games", "Real Saves")));
    }

    [Theory]
    [InlineData("saves/COM\u00b9.sav")]
    [InlineData("saves/lpt\u00b3")]
    [InlineData("saves/CON.txt")]
    [InlineData("saves/sub/NUL")]
    public void R6_names_Windows_keeps_for_devices_are_refused_superscript_digits_too(string path) =>
        Assert.Throws<UnsafePathException>(() => RestorePathGuard.Split(path));

    [Theory]
    [InlineData(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Run")]
    [InlineData(@"HKCU\Software\Microsoft\Windows\CurrentVersion\RunOnce")]
    [InlineData(@"HKCU\Software\Microsoft\Windows\CurrentVersion\RunServices")]
    [InlineData(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer")]
    [InlineData(@"HKCU\Software\Microsoft\Windows NT\CurrentVersion\Winlogon")]
    [InlineData(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders")]
    [InlineData(@"HKCU\Software\Classes\exefile\shell\open\command")]
    [InlineData(@"HKLM\Software\Lantern Studio\Lantern Keep")]
    [InlineData(@"HKCU\Software\Other Studio\Other Game")]
    [InlineData(@"HKCU\Software\Lantern Studio\Lantern Keep\..\..\Microsoft\Windows\CurrentVersion\Run")]
    public void R7_registry_restores_touch_only_the_games_own_key(string key)
    {
        const string Own = @"HKCU\Software\Lantern Studio\Lantern Keep";
        Assert.NotNull(RegistryGuard.Check(key, [Own]));

        // Not even a rule from the cloud that names a startup key makes one restorable.
        Assert.NotNull(RegistryGuard.Check(@"HKCU\Software\Microsoft\Windows\CurrentVersion\RunOnce", [@"HKCU\Software\Microsoft\Windows\CurrentVersion\RunOnce"]));
        Assert.Null(RegistryGuard.Check(Own + @"\Settings", [Own]));
    }

    [Fact]
    public async Task R8_another_PCs_rules_that_reach_a_protected_folder_are_never_taken_up()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        new AppConfig { Remote = world.Cloud, Games = [] }.Save(data);
        var saves = Path.Combine(world.Root, "Lantern Keep", "Saves");
        Directory.CreateDirectory(saves);
        using (var library = new LibraryStore(data))
        {
            library.SaveAll([Entry(saves)]);
        }

        // Another PC's version says the game keeps its saves in .ssh. Sync these saves takes another PC's rules up (PC-04),
        // after the same checks as any rule (R5), so these aren't taken and nothing syncs.
        var ssh = RootResolver.ToPortable(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh"), Cli.FoldersForThisPc());
        using (var state = new Core.State.StateStore(data))
        {
            var history = new Core.Storage.LocalHistory(Cli.HistoryFolder(state, data));
            await history.Log.AppendAsync(Lantern, new VersionRecord
            {
                Id = VersionId.New(DateTime.UtcNow, "LAPTOP"),
                Game = Lantern,
                Kind = VersionKind.Normal,
                Origin = VersionOrigin.Session,
                Device = new DeviceInfo(DeviceId.Parse("d-laptop"), "LAPTOP"),
                CreatedUtc = DateTime.UtcNow,
                Rules = new PortableRules { Title = "Lantern Keep", Roots = new Dictionary<string, string> { ["saves"] = ssh }, Rules = [new SaveRule { Root = "saves" }] },
                Files = [],
            }, Ct);
        }

        var output = new Recorded();
        Assert.False(await AppActions.SyncGameAsync(data, Lantern, output, Ct));
        Assert.Contains(output.NeedsYouLines, l => l.StartsWith("Lantern Keep: Its saves can't sync yet: ", StringComparison.Ordinal) && l.Contains(".ssh", StringComparison.OrdinalIgnoreCase));
        using (var library = new LibraryStore(data))
        {
            Assert.Null(library.All().Single().Confirmed);
        }
    }

    [Fact]
    public void R10_tokens_are_encrypted_for_this_Windows_user_alone()
    {
        // DPAPI's blob says whose key made it: flag 4 is the PC's, which every user of the PC can open.
        const int Flags = 40;
        var blob = new GameSync.Windows.DpapiProtector().Protect("a sign-in token"u8.ToArray());
        Assert.Equal(0u, BitConverter.ToUInt32(blob, Flags) & 4);
        var machine = ProtectedData.Protect("a sign-in token"u8.ToArray(), null, DataProtectionScope.LocalMachine);
        Assert.Equal(4u, BitConverter.ToUInt32(machine, Flags) & 4);
    }

    [Fact]
    public void R11_only_the_sign_in_redirect_listens_and_only_on_127_0_0_1()
    {
        foreach (var file in SourceFiles())
        {
            var text = File.ReadAllText(file);
            var listens = new[] { "TcpListener", "HttpListener", "UdpClient", "new Socket(", "WebApplication", "UseKestrel" }
                .Where(api => text.Contains(api, StringComparison.Ordinal)).ToList();
            Assert.True(listens.Count == 0 || (Path.GetFileName(file) == "LoopbackRedirect.cs" && listens is ["TcpListener"]), $"{file} uses {string.Join(", ", listens)}");
        }

        using var redirect = new LoopbackRedirect();
        Assert.Equal(IPAddress.Loopback, redirect.Endpoint.Address);
    }

    [Fact]
    public async Task R11_the_apps_pipe_answers_a_program_on_this_PC()
    {
        // A client on another PC (the pipe reached as \\this-pc\pipe\…) is turned away; this one, here, is answered.
        var name = "GameSync-test-" + Guid.NewGuid().ToString("N");
        await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var waiting = server.WaitForConnectionAsync(Ct);
        await client.ConnectAsync(5000, Ct);
        await waiting;
        Assert.True(AppPipe.IsLocal(server));
    }

    [Fact]
    public async Task R12_how_a_game_starts_on_this_PC_never_leaves_it()
    {
        using var world = new TestWorld();
        var (data, saves) = await SyncedGame(world);
        var program = Path.Combine(world.Root, "Lantern Keep", "LanternKeep.exe");
        File.WriteAllBytes(program, SafetyTests.FakeProgram());
        using (var state = new Core.State.StateStore(data))
        {
            GameLaunch.SetProgram(state, Lantern, program);
            GameLaunch.SetOptions(state, Lantern, "-windowed -launch-marker-0f3a");
        }

        File.WriteAllText(Path.Combine(saves, "slot1.sav"), "past the lighthouse");
        await AppActions.BackUpNowAsync(data, Lantern, new Quiet(), Ct);
        var zipPath = Path.Combine(world.Root, "shared.zip");
        await Sharing.PackAsync(data, [new SharePick(Lantern, [Assert.Single(await Sharing.ListAsync(data, Ct)).Latest!.Id])], zipPath, null, new Quiet(), Ct);

        // Nothing in the cloud's records, nor in a shared zip, says how the game starts on this PC.
        var said = Directory.EnumerateFiles(world.Cloud, "*.json", SearchOption.AllDirectories).Select(File.ReadAllText).ToList();
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            said.AddRange(zip.Entries.Where(e => !e.FullName.EndsWith(".sav", StringComparison.Ordinal)).Select(e =>
            {
                using var reader = new StreamReader(e.Open());
                return reader.ReadToEnd();
            }));
        }

        Assert.NotEmpty(said);
        Assert.DoesNotContain(said, text => text.Contains("launch-marker-0f3a", StringComparison.Ordinal) || text.Contains("LanternKeep.exe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void R13_game_processes_are_opened_with_query_limited_access_only_and_nothing_reaches_into_them()
    {
        string[] banned = ["ReadProcessMemory", "WriteProcessMemory", "VirtualAllocEx", "CreateRemoteThread", "QueueUserAPC", "SetWindowsHookEx",
            "DebugActiveProcess", "SuspendThread", "NtSuspendProcess", "OpenThread", "GetProcessById", ".MainModule", ".Modules"];
        foreach (var file in SourceFiles())
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain(banned, call => text.Contains(call, StringComparison.Ordinal));
            foreach (Match open in Regex.Matches(text, @"OpenProcess\(\s*(\w+)"))
            {
                Assert.True(open.Groups[1].Value is "QueryLimitedInformation" or "uint", $"{file} opens a process with {open.Groups[1].Value}");
            }
        }
    }

    [Fact]
    public void R13_GameSync_draws_no_popup_over_a_game_with_an_anti_cheat()
    {
        // Achievements by game: its popup is off and can't be turned on, and it says why.
        var row = new AchievementGameRow(Lantern, "Lantern Keep", "3 of 10", LeftOut: false, Cover: null, Popup: true, AntiCheat: true);
        var item = new AchievementGameItem(row, (_, _) => Task.CompletedTask);
        Assert.False(item.CanPopup);
        Assert.False(item.PopupShown);
        item.PopupShown = true;
        Assert.False(item.PopupShown);
        Assert.Equal("3 of 10 · Has an anti-cheat: GameSync draws nothing over it", item.Line);

        // The app knows which games those are from the library.
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        using (var library = new LibraryStore(data))
        {
            library.SaveAll([new LibraryEntry { Id = Lantern, Title = "Lantern Keep", FirstSeenUtc = DateTime.UtcNow, AntiCheat = "EasyAntiCheat" }]);
        }

        Assert.Equal([Lantern], Achievements.AntiCheatGames(data));
    }

    [Fact]
    public void R14_there_is_no_kernel_driver_and_no_shared_ETW_session()
    {
        var root = RepoRoot();
        Assert.Empty(new[] { "*.sys", "*.inf", "*.cat" }.SelectMany(p => Directory.EnumerateFiles(Path.Combine(root, "src"), p, SearchOption.AllDirectories)).Where(NotBuildOutput));
        foreach (var file in SourceFiles())
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("NT Kernel Logger", text, StringComparison.Ordinal);
            Assert.DoesNotContain("KERNEL_LOGGER_NAME", text, StringComparison.Ordinal);
            Assert.DoesNotContain("SERVICE_KERNEL_DRIVER", text, StringComparison.Ordinal);
        }

        // Learn mode watches files (FileSystemWatcher); the tracer and its own ETW session come later (FIND-05).
        foreach (var project in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories))
        {
            Assert.DoesNotContain("TraceEvent", File.ReadAllText(project), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void R15_a_game_with_an_anti_cheat_starts_only_through_its_store_or_its_anti_cheats_own_launcher()
    {
        using var world = new TestWorld();
        var folder = Directory.CreateDirectory(Path.Combine(world.Root, "Rogue Arena")).FullName;
        var game = Path.Combine(folder, "RogueArena.exe");
        File.WriteAllBytes(game, SafetyTests.FakeProgram());
        var entry = new LibraryEntry
        {
            Id = GameId.Parse("rogue-arena"), Title = "Rogue Arena", FirstSeenUtc = DateTime.UtcNow, Installed = true, Store = StoreKind.Loose,
            InstallDir = folder, AntiCheat = "EasyAntiCheat",
        };

        // With no anti-cheat launcher in its folder, GameSync doesn't start it at all, the program picked or not.
        var refused = Assert.Throws<UsageException>(() => Cli.Route("Rogue Arena", entry, folder, game, null));
        Assert.Contains("Start it from its launcher", refused.Message);

        // Easy Anti-Cheat's own launcher is the way, through the shell, even with the game's program picked.
        var launcher = Path.Combine(folder, GameLaunch.EacLauncher);
        File.WriteAllBytes(launcher, SafetyTests.FakeProgram());
        var start = Cli.Route("Rogue Arena", entry, folder, game, "-windowed");
        Assert.Equal((launcher, true, "-windowed"), (start.FileName, start.UseShellExecute, start.Arguments));
        Assert.Throws<UsageException>(() => GameLaunch.CheckProgram(game, antiCheat: true));
        Assert.Equal(launcher, GameLaunch.CheckProgram(launcher, antiCheat: true));
        Assert.Equal(game, GameLaunch.CheckProgram(game, antiCheat: false));
        Assert.True(GameLaunch.IsAntiCheatLauncher(Path.Combine(folder, "RogueArena_BE.exe")));

        // A store's game starts through its store; one with no anti-cheat from its program.
        Assert.Equal("steam://rungameid/1172470", Cli.Route("Rogue Arena", entry with { Store = StoreKind.Steam, StoreId = "1172470" }, folder, null, null).FileName);
        Assert.Equal(game, Cli.Route("Rogue Arena", entry with { AntiCheat = null }, folder, game, null).FileName);
    }

    [Fact]
    public void R16_a_game_with_an_anti_cheat_or_that_plays_only_online_is_never_shared()
    {
        var entry = new LibraryEntry { Id = Lantern, Title = "Lantern Keep", FirstSeenUtc = DateTime.UtcNow };
        Assert.Null(Sharing.Blocked(entry));
        Assert.Equal("Plays online, so its saves can't be shared.", Sharing.Blocked(entry with { ProbablyOnlineOnly = true }));
        Assert.Equal("Ships an anti-cheat, so its saves can't be shared.", Sharing.Blocked(entry with { AntiCheat = "BattlEye" }));
    }

    [Fact]
    public void R17_GameSync_never_asks_Windows_for_more_than_the_persons_own_rights()
    {
        var root = RepoRoot();
        var manifests = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.manifest", SearchOption.AllDirectories).Where(NotBuildOutput).ToList();
        Assert.Contains("level=\"asInvoker\"", File.ReadAllText(Assert.Single(manifests, m => m.Contains("GameSync.Tray", StringComparison.Ordinal))), StringComparison.Ordinal);
        foreach (var file in SourceFiles().Concat(manifests))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("requireAdministrator", text, StringComparison.Ordinal);
            Assert.DoesNotContain("highestAvailable", text, StringComparison.Ordinal);
            Assert.DoesNotContain("\"runas\"", text, StringComparison.OrdinalIgnoreCase);
        }

        // Started as an administrator anyway, the app says so and closes, before it takes its place as the running app.
        var program = File.ReadAllText(Path.Combine(root, "src", "GameSync.Tray", "Program.cs"));
        var refusal = program.IndexOf("Elevation.IsElevated()", StringComparison.Ordinal);
        Assert.InRange(refusal, 1, program.IndexOf("AppPipe.TryClaim", StringComparison.Ordinal));
    }

    [Fact(Skip = "The tracer isn't built yet: it comes with the installer (FIND-05, KAN-34), and so do its tests: only during learn mode, under Program Files, signature-checked, a pipe for this user only, no command that writes.")]
    public void R18_the_tracer_runs_only_during_learn_mode_from_Program_Files_signed_and_read_only()
    {
    }

    [Fact]
    public void R19_releases_are_signed_and_the_updater_refuses_one_that_isnt()
    {
        // No Windows code signing (the owner, 7 Oct 2026): each release's installer carries GameSync's own release
        // signature, which the key built into GameSync checks; one signed with any other key, or not at all, is refused
        // (UpdateTests has the installer changed on the way, and checked again as it starts).
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var version = new Version(9, 9, 9);
        var manifest = new ReleaseManifest(version, ReleaseManifest.NameFor(version), 1, new string('0', 64));
        Assert.NotEmpty(ReleaseKey.Public);
        Assert.Throws<ReleaseRefusedException>(() => ReleaseManifest.Verify(manifest.Sign(other), ReleaseKey.Public));
        Assert.Throws<ReleaseRefusedException>(() => ReleaseManifest.Verify(manifest.Body, ReleaseKey.Public));

        // The key's private half never goes into the repository: it's kept in notes\, which git ignores.
        var root = RepoRoot();
        Assert.Contains("notes/", File.ReadAllLines(Path.Combine(root, ".gitignore")));
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .Where(f => NotBuildOutput(f) && !f.Contains($"{Path.DirectorySeparatorChar}notes{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                         && !f.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                         && !f.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                         && Path.GetExtension(f) is ".cs" or ".ps1" or ".iss" or ".json" or ".md" or ".pem" or ".txt" or ".yml" or ".props"))
        {
            Assert.DoesNotContain("PRIVATE" + " KEY-----", File.ReadAllText(file), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void R20_every_project_pins_its_packages_and_CI_restores_them_locked_with_pinned_actions_and_a_read_only_token()
    {
        var root = RepoRoot();
        foreach (var project in new[] { "src", "tests", "tools" }.SelectMany(d => Directory.EnumerateFiles(Path.Combine(root, d), "*.csproj", SearchOption.AllDirectories)).Where(NotBuildOutput))
        {
            Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(project)!, "packages.lock.json")), $"{project} has no packages.lock.json");
        }

        Assert.Contains("<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>", File.ReadAllText(Path.Combine(root, "Directory.Build.props")), StringComparison.Ordinal);
        var workflows = Directory.EnumerateFiles(Path.Combine(root, ".github", "workflows"), "*.yml").ToList();
        Assert.NotEmpty(workflows);
        foreach (var workflow in workflows)
        {
            var text = File.ReadAllText(workflow);
            Assert.Matches(@"(?m)^permissions:\s*\r?\n\s+contents: read\s*$", text);
            Assert.Contains("--locked-mode", text, StringComparison.Ordinal);
            foreach (Match uses in Regex.Matches(text, @"uses:\s*(\S+)"))
            {
                Assert.Matches(@"^[\w.-]+/[\w.-]+@[0-9a-f]{40}$", uses.Groups[1].Value);
            }
        }
    }

    [Fact(Skip = "The server is for later (R21, design.md → Server): ownership on every request, a limit per person, a spending alert, short-lived upload links scoped to one folder, no client deletes, encryption on; tested in its own suite.")]
    public void R21_the_server_checks_ownership_and_limits_and_never_lets_a_client_delete()
    {
    }

    private static LibraryEntry Entry(string saves) => new()
    {
        Id = Lantern,
        Title = "Lantern Keep",
        FirstSeenUtc = DateTime.UtcNow,
        Installed = true,
        Store = StoreKind.Loose,
        Proposals = [new Proposal(FoundBy.SaveList, RootResolver.ToPortable(saves, Cli.FoldersForThisPc()), "**", SaveCategory.Save, 1, 20, null)],
    };

    /// <summary>A junction from <paramref name="link"/> to <paramref name="target"/>, as anyone can make without admin.</summary>
    private static void Junction(string link, string target)
    {
        using var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        })!;
        mklink.WaitForExit();
        Assert.True(new DirectoryInfo(link).LinkTarget is not null, "the junction wasn't made");
    }

    private static string RepoRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "GameSync.sln")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new InvalidOperationException("The repository wasn't found above the tests.");
    }

    /// <summary>GameSync's own source, not what builds make.</summary>
    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src"), "*.cs", SearchOption.AllDirectories).Where(NotBuildOutput);

    private static bool NotBuildOutput(string path) =>
        !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
        !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);


    /// <summary>A data folder where Lantern Keep syncs, with its first backup made.</summary>
    private static async Task<(string Data, string Saves)> SyncedGame(TestWorld world)
    {
        var data = Path.Combine(world.Root, "data");
        new AppConfig { Remote = world.Cloud, Games = [] }.Save(data);
        var saves = Path.Combine(world.Root, "Lantern Keep", "Saves");
        Directory.CreateDirectory(saves);
        File.WriteAllText(Path.Combine(saves, "slot1.sav"), "at the lighthouse");
        using (var library = new LibraryStore(data))
        {
            library.SaveAll([new LibraryEntry
            {
                Id = Lantern,
                Title = "Lantern Keep",
                FirstSeenUtc = DateTime.UtcNow,
                Installed = true,
                Store = StoreKind.Loose,
                Proposals = [new Proposal(FoundBy.SaveList, RootResolver.ToPortable(saves, Cli.FoldersForThisPc()), "**", SaveCategory.Save, 1, 20, null)],
            }]);
        }

        Assert.True(await AppActions.SyncGameAsync(data, Lantern, new Quiet(), Ct));
        await AppActions.BackUpNowAsync(data, Lantern, new Quiet(), Ct);
        return (data, saves);
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

    private sealed class Recorded : IAgentOutput
    {
        public List<string> NeedsYouLines { get; } = [];

        public void Say(string line)
        {
        }

        public void NeedsYou(string title, string message) => NeedsYouLines.Add($"{title}: {message}");
    }

    /// <summary>No sign-in at all: enough to build a request, which never goes out.</summary>
    private sealed class NoCredential : Google.Apis.Http.IConfigurableHttpClientInitializer
    {
        public void Initialize(Google.Apis.Http.ConfigurableHttpClient httpClient)
        {
        }
    }
}
