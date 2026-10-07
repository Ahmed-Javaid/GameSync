using System.Net;
using System.Security.Cryptography;
using System.Text;
using GameSync.Core.Discovery;
using GameSync.Core.Model;
using GameSync.Core.Storage;
using GameSync.Host;
using Microsoft.Win32;

namespace GameSync.Core.Tests;

/// <summary>
/// PKG-01, PKG-03, PKG-05, R12, R19 (design system version 53; the owner, 7 Oct 2026: no paid code signing, "yes,
/// per-user install and tracer after v1"): GameSync asks GitHub for a newer release once a day, gets it ready only when
/// its own release signature checks out, and installs it on Restart to update; the uninstaller takes away only what
/// starts this copy; a gamesync:// link starts only a game this PC knows.
/// </summary>
public class UpdateTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly Version Next = new(1, 0, 1);
    private const string Feed = "https://api.github.com/repos/Ahmed-Javaid/GameSync/releases/latest";
    private const string Downloads = "https://github.com/Ahmed-Javaid/GameSync/releases/download/v1.0.1/";
    private const string AssetHost = "https://release-assets.githubusercontent.com/github-production-release-asset/";

    [Fact]
    public void R19_a_release_signed_with_the_key_checks_out_and_anything_else_is_refused()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        var manifest = new ReleaseManifest(Next, ReleaseManifest.NameFor(Next), 3, Convert.ToHexStringLower(SHA256.HashData("abc"u8)));
        var signed = manifest.Sign(key);

        Assert.Equal(manifest, ReleaseManifest.Verify(signed, publicKey));
        Assert.True(manifest.Describes(new MemoryStream("abc"u8.ToArray())));
        Assert.False(manifest.Describes(new MemoryStream("abd"u8.ToArray())));
        Assert.False(manifest.Describes(new MemoryStream("abcd"u8.ToArray())));

        Refused("isn't signed with GameSync's release key", signed.Replace("size 3", "size 4", StringComparison.Ordinal), publicKey);
        Refused("isn't signed with GameSync's release key", manifest.Sign(other), publicKey);
        Refused("carries no signature", manifest.Body, publicKey);
        Refused("names another file", (manifest with { File = "Evil.exe" }).Sign(key), publicKey);
        var fourParts = manifest.Body.Replace("version 1.0.1", "version 1.0.1.5", StringComparison.Ordinal);
        Refused("version can't be read", fourParts + "signature " + Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes(fourParts), HashAlgorithmName.SHA256)) + "\n", publicKey);
        Refused("no release key built in", signed, "");

        // GameSync's own key is built in and readable; a release signed with any other key is refused by it.
        Assert.NotEmpty(ReleaseKey.Public);
        Refused("isn't signed with GameSync's release key", signed, ReleaseKey.Public);
    }

    [Fact]
    public async Task PKG_03_a_newer_release_comes_down_checked_and_waits_for_Restart_to_update()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var installer = Encoding.UTF8.GetBytes(new string('x', 200_000));
        var asked = new List<string>();
        var now = new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
        using var updates = new Updates(data, Serve(key, installer, asked), Public(key), new Version(1, 0, 0), utcNow: () => now);

        Assert.True(updates.Due());
        var reports = new List<UpdateProgress>();
        var view = await updates.CheckAndGetAsync(new SyncProgress<UpdateProgress>(reports.Add), Ct);

        Assert.Equal(Next, view.Ready?.Version);
        Assert.Equal(["Restoring a named save says which file a game holds.", "Home's Zenith is a dot."], view.Ready!.Notes);
        Assert.Equal("https://github.com/Ahmed-Javaid/GameSync/releases/tag/v1.0.1", view.Ready.Page);
        Assert.Equal(installer, File.ReadAllBytes(view.Ready.Installer));
        Assert.Equal(new UpdateProgress(Next, 200_000, 200_000), reports[^1]);
        Assert.Null(view.Refused);
        Assert.Null(view.Problem);
        Assert.Equal(now, view.CheckedUtc);

        // The signature came first, then the installer, through GitHub's download servers.
        Assert.Equal([Feed, Downloads + "GameSync-Setup-1.0.1.sig", AssetHost + "sig", Downloads + "GameSync-Setup-1.0.1.exe", AssetHost + "exe"], asked);

        // Not asked again for a day; once it's installed (this GameSync is 1.0.1), it's not offered, and the download goes.
        Assert.False(updates.Due());
        now = now.AddDays(1);
        Assert.True(updates.Due());
        using var updated = new Updates(data, Serve(key, installer, asked), Public(key), Next);
        Assert.Null(updated.FindReady());
        updated.CleanUp();
        Assert.Empty(Directory.GetFiles(updated.Folder));

        // Off, it isn't asked at all.
        Updates.SetDaily(data, false);
        Assert.False(updates.Due());
        Assert.False(updates.Read().Daily);
    }

    [Fact]
    public async Task R19_an_update_changed_on_the_way_or_not_from_GitHub_is_refused_and_nothing_is_kept()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var installer = Encoding.UTF8.GetBytes(new string('x', 50_000));

        async Task<UpdatesView> Try(HttpMessageHandler handler, string reason)
        {
            using var world = new TestWorld();
            var data = Path.Combine(world.Root, "data");
            using var updates = new Updates(data, handler, Public(key), new Version(1, 0, 0));
            var view = await updates.CheckAndGetAsync(null, Ct);
            Assert.Null(view.Ready);
            Assert.Contains(reason, view.Refused?.Reason ?? view.Problem?.Reason ?? "", StringComparison.Ordinal);
            Assert.Empty(Directory.Exists(updates.Folder) ? Directory.GetFiles(updates.Folder, "*.exe*") : []);
            return view;
        }

        // Changed on the way: other bytes than the signature describes, more of them, or signed by another key.
        await Try(Serve(key, installer, [], served: installer.Select(b => (byte)'y').ToArray()), "may have been changed on the way");
        await Try(Serve(key, installer, [], served: [.. installer, .. installer]), "bigger than its signature says");
        var refused = await Try(Serve(other, installer, []), "isn't signed with GameSync's release key");
        Assert.Equal(Next, refused.Refused?.Version);

        // Sent anywhere but GitHub, or over plain HTTP.
        await Try(Serve(key, installer, [], assetHost: "https://downloads.example.com/"), "which isn't GitHub");
        await Try(Serve(key, installer, [], assetHost: "http://release-assets.githubusercontent.com/"), "which isn't GitHub");
    }

    [Fact]
    public async Task PKG_03_nothing_newer_offline_or_no_release_yet_installs_nothing_and_says_why()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var now = new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

        // The same version or an older one: nothing to do.
        using (var same = new Updates(data, Serve(key, [1, 2, 3], []), Public(key), Next))
        {
            var view = await same.CheckAndGetAsync(null, Ct);
            Assert.Null(view.Ready);
            Assert.Null(view.Problem);
        }

        // No release yet.
        using (var none = new Updates(data, new AchievementTests.Steam(_ => new HttpResponseMessage(HttpStatusCode.NotFound)), Public(key), new Version(1, 0, 0)))
        {
            Assert.Null((await none.CheckAndGetAsync(null, Ct)).Problem);
        }

        // Offline: it says so, and asks again in six hours rather than a day.
        using var offline = new Updates(data, new AchievementTests.Steam(_ => throw new HttpRequestException(HttpRequestError.ConnectionError, "No connection")),
            Public(key), new Version(1, 0, 0), utcNow: () => now);
        var off = await offline.CheckAndGetAsync(null, Ct);
        Assert.True(off.Problem?.Offline);
        Assert.Contains("offline", off.Problem!.Reason, StringComparison.Ordinal);
        now = now.AddHours(5);
        Assert.False(offline.Due());
        now = now.AddHours(1);
        Assert.True(offline.Due());
    }

    [Fact]
    public async Task R19_Restart_to_update_checks_the_installer_again_as_it_starts()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var installer = Encoding.UTF8.GetBytes(new string('x', 4096));
        using var updates = new Updates(data, Serve(key, installer, []), Public(key), new Version(1, 0, 0));
        var ready = (await updates.CheckAndGetAsync(null, Ct)).Ready!;

        // Changed after it came down, the same size: refused, and nothing starts.
        File.WriteAllBytes(ready.Installer, installer.Select(b => (byte)'z').ToArray());
        var e = Assert.Throws<ReleaseRefusedException>(() => updates.StartInstall(ready, showWindow: true));
        Assert.Contains("isn't the installer its signature describes", e.Message, StringComparison.Ordinal);

        // The installer is told to stay quiet, close GameSync and open it again on this data folder.
        Assert.Equal(["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CLOSEAPPLICATIONS", "/RELAUNCH=window"], Updates.InstallerArguments(true, Engine.DefaultDataDir));
        Assert.Equal($"/DATA={Path.GetFullPath(data)}", Updates.InstallerArguments(false, data)[^1]);
        Assert.Equal("/RELAUNCH=background", Updates.InstallerArguments(false, data)[^2]);
    }

    [Fact]
    public void PKG_03_whats_new_is_the_release_notes_bullets_in_plain_words()
    {
        Assert.Equal(["Restores say which file", "See the docs", "A gamesync verb"],
            Updates.NotesOf("## What's new\r\n- Restores say **which** file\n\n* See [the docs](https://x)\n- A `gamesync` verb\nPlain line"));
        Assert.Equal(Next, Updates.VersionOfTag("v1.0.1"));
        Assert.Equal(Next, Updates.VersionOfTag("1.0.1"));
        Assert.Null(Updates.VersionOfTag("v1.0"));
        Assert.Null(Updates.VersionOfTag("latest"));
    }

    [Fact]
    public void R12_a_link_names_one_action_and_a_game_and_nothing_else()
    {
        Assert.Equal((Links.Action.Play, GameId.Parse("terraria")), Links.Parse("gamesync://play/terraria"));
        Assert.Equal((Links.Action.Open, GameId.Parse("hollow-knight")), Links.Parse("gamesync://open/hollow-knight/"));
        foreach (var link in new[]
        {
            "gamesync://play/", "gamesync://delete/terraria", "gamesync://play/terraria/extra", "gamesync://play/terraria?then=calc",
            "gamesync://play/terraria#x", "gamesync://play/..%5Cterraria", "gamesync://play/ter%20raria", "gamesync://user@play/terraria",
            "gamesync://play:8080/terraria", "https://play/terraria", "gamesync:play/terraria", "", "not a link",
        })
        {
            Assert.True(Links.Parse(link) is null, link);
        }
    }

    [Fact]
    public async Task R12_a_link_starts_only_a_game_this_PC_knows()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        new AppConfig { Remote = world.Cloud, Games = [] }.Save(data);
        using (var library = new LibraryStore(data))
        {
            library.SaveAll([
                new LibraryEntry { Id = GameId.Parse("terraria"), Title = "Terraria", FirstSeenUtc = DateTime.UtcNow, Installed = true, Store = StoreKind.Loose },
                new LibraryEntry { Id = GameId.Parse("ignored"), Title = "Ignored", FirstSeenUtc = DateTime.UtcNow, State = LibraryState.Ignored },
            ]);
        }

        Assert.True(Links.Knows(data, GameId.Parse("terraria")));
        Assert.False(Links.Knows(data, GameId.Parse("ignored")));
        Assert.False(Links.Knows(data, GameId.Parse("calc")));

        var output = new Told();
        Assert.Equal(2, await Links.OpenAsync(data, "gamesync://play/calc", output));
        Assert.Contains("doesn't know", output.Lines.Single(), StringComparison.Ordinal);
        Assert.Equal(2, await Links.OpenAsync(data, "gamesync://run/cmd.exe", output));
        Assert.Contains("isn't one GameSync opens", output.Lines[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task PKG_05_uninstalling_takes_away_only_what_starts_this_copy()
    {
        var folder = Path.Combine(Path.GetTempPath(), "gamesync-tests", "Programs", "GameSync");
        Assert.True(Install.Starts($"\"{folder}\\GameSync.Tray.exe\" --background", folder));
        Assert.True(Install.Starts($"{folder}\\GameSync.Tray.exe", folder));
        Assert.False(Install.Starts($"\"{folder}2\\GameSync.Tray.exe\" --background", folder));
        Assert.False(Install.Starts(@"""E:\Games\Tools\GameSync\src\GameSync.Tray\bin\GameSync.Tray.exe"" --background", folder));
        Assert.False(Install.Starts(null, folder));

        // The Run key and the tasks, in a registry key of the test's own.
        var test = $@"Software\GameSyncTests\{Guid.NewGuid():N}";
        try
        {
            var signIn = new SignInStartFor(test);
            signIn.Start.TurnOn($"\"{folder}\\GameSync.Tray.exe\" --background");
            var tasks = new Dictionary<string, string?>
            {
                ["GameSync daily backup"] = $"{folder}\\GameSync.Tray.exe",
                ["GameSync catch-up"] = @"D:\Other copy\GameSync.Tray.exe",
                ["GameSync background"] = null,
            };
            var removed = new List<string>();
            using var world = new TestWorld();
            var said = await Install.CleanUpAsync(folder, Path.Combine(world.Root, "data"), signIn.Start, name => tasks.GetValueOrDefault(name), removed.Add);

            Assert.Null(signIn.Start.Command);
            Assert.Equal(["GameSync daily backup"], removed);
            Assert.Contains("Your backups, your settings and your cloud copy stay where they are.", said);

            // Another copy's start stays.
            signIn.Start.TurnOn(@"""D:\Other copy\GameSync.Tray.exe"" --background");
            await Install.CleanUpAsync(folder, Path.Combine(world.Root, "data"), signIn.Start, _ => null, removed.Add);
            Assert.NotNull(signIn.Start.Command);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(test, false);
        }
    }

    private static void Refused(string reason, string text, string publicKey)
    {
        var e = Assert.Throws<ReleaseRefusedException>(() => ReleaseManifest.Verify(text, publicKey));
        Assert.Contains(reason, e.Message, StringComparison.Ordinal);
    }

    private static string Public(ECDsa key) => Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

    /// <summary>
    /// GitHub as the updater sees it: the latest release 1.0.1 with its notes, and its installer and signature, each sent
    /// on from github.com to the download servers (<paramref name="assetHost"/>). <paramref name="served"/> is what comes
    /// down as the installer, when it isn't what was signed.
    /// </summary>
    private static AchievementTests.Steam Serve(ECDsa key, byte[] installer, List<string> asked, byte[]? served = null, string assetHost = AssetHost)
    {
        var signed = new ReleaseManifest(Next, ReleaseManifest.NameFor(Next), installer.Length, Convert.ToHexStringLower(SHA256.HashData(installer))).Sign(key);
        return new AchievementTests.Steam(url =>
        {
            asked.Add(url);
            if (url == Feed)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                        {"tag_name":"v1.0.1","html_url":"https://github.com/Ahmed-Javaid/GameSync/releases/tag/v1.0.1","draft":false,"prerelease":false,
                         "body":"## What's new\n- Restoring a named save says which file a game holds.\n- Home's Zenith is a dot.",
                         "assets":[{"name":"GameSync-Setup-1.0.1.exe","size":{{installer.Length}},"browser_download_url":"{{Downloads}}GameSync-Setup-1.0.1.exe"},
                                   {"name":"GameSync-Setup-1.0.1.sig","size":300,"browser_download_url":"{{Downloads}}GameSync-Setup-1.0.1.sig"}]}
                        """),
                };
            }

            if (url.StartsWith(Downloads, StringComparison.Ordinal))
            {
                var response = new HttpResponseMessage(HttpStatusCode.Found);
                response.Headers.Location = new Uri(assetHost + (url.EndsWith(".sig", StringComparison.Ordinal) ? "sig" : "exe"));
                return response;
            }

            return url.EndsWith("sig", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(signed) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(served ?? installer) };
        });
    }

    /// <summary>Progress reported as it happens, not posted to a thread later.</summary>
    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class SignInStartFor(string root)
    {
        public GameSync.Windows.SignInStart Start { get; } = new(Registry.CurrentUser, root + @"\Run", root + @"\Approved");
    }

    private sealed class Told : IAgentOutput
    {
        public List<string> Lines { get; } = [];

        public void Say(string line) => Lines.Add(line);

        public void NeedsYou(string title, string message) => Lines.Add(message);
    }
}
