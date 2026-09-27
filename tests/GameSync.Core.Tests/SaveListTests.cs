using System.Diagnostics;
using GameSync.Core.Discovery;
using Xunit.Abstractions;

namespace GameSync.Core.Tests;

/// <summary>FIND-01: the PCGamingWiki save list (the Ludusavi manifest), read and looked up.</summary>
public class SaveListTests(ITestOutputHelper output)
{
    private const string Manifest = """
        ---
        Terraria:
          cloud:
            steam: true
          files:
            "<home>/Library/Application Support/Terraria":
              tags:
                - save
              when:
                - os: mac
            "<root>/userdata/<storeUserId>/105600/remote/players/*.plr":
              tags:
                - save
              when:
                - store: steam
            "<winDocuments>/My Games/Terraria":
              tags:
                - save
              when:
                - os: windows
            "<winDocuments>/My Games/Terraria/config.json":
              tags:
                - config
              when:
                - os: windows
          installDir:
            Terraria: {}
          steam:
            id: 105600
        "Black Myth: Wukong":
          cloud:
            epic: true
            steam: true
          files:
            "<base>/b1/Saved/SaveGames/<storeUserId>/*.sav":
              tags:
                - save
              when:
                - os: windows
          id:
            steamExtra:
              - 2672610
          installDir:
            BlackMythWukong: {}
          steam:
            id: 2358720
        Cuphead:
          cloud:
            gog: true
          files:
            "<home>/AppData/LocalLow/Studio MDHR/Cuphead":
              tags:
                - save
          gog:
            id: 1963513391
          registry:
            HKEY_CURRENT_USER/Software/Studio MDHR/Cuphead:
              tags:
                - config
        Old Name Of Terraria:
          alias: Terraria
        Linux Only Game:
          files:
            "<xdgData>/LinuxOnly":
              when:
                - os: linux
          steam:
            id: 999
        Nothing Useful: {}
        """;

    [Fact]
    public void FIND_01_the_save_list_keeps_what_Windows_needs()
    {
        var list = SaveListParser.Parse(new StringReader(Manifest), "test", DateTime.UtcNow);

        var terraria = list.BySteamId(105600)!;
        Assert.Equal("Terraria", terraria.Title);
        Assert.Equal(["steam"], terraria.Cloud);
        Assert.Equal(3, terraria.Files.Count);
        Assert.DoesNotContain(terraria.Files, f => f.Path.Contains("Library", StringComparison.Ordinal));
        Assert.Equal(["steam"], terraria.Files.Single(f => f.Path.StartsWith("<root>", StringComparison.Ordinal)).Stores);
        Assert.Null(terraria.Files.Single(f => f.Path == "<winDocuments>/My Games/Terraria").Stores);
        var config = terraria.Files.Single(f => f.Path.EndsWith("config.json", StringComparison.Ordinal));
        Assert.True(config.Config);
        Assert.False(config.Save);

        Assert.Same(list.BySteamId(2358720), list.BySteamId(2672610));
        Assert.Equal("Cuphead", list.ByGogId(1963513391)!.Title);
        Assert.Equal("HKEY_CURRENT_USER/Software/Studio MDHR/Cuphead", Assert.Single(list.ByGogId(1963513391)!.Registry).Path);
        Assert.Empty(list.BySteamId(999)!.Files);
        Assert.Null(list.ByTitle("Nothing Useful"));
    }

    [Theory]
    [InlineData("SlayTheSpire2", "Slay the Spire 2")]
    [InlineData("BLACK MYTH: WUKONG", "Black Myth: Wukong")]
    [InlineData("God of War Ragnarök", "God of War Ragnarok")]
    public void LIB_06_names_compare_as_letters_and_digits(string a, string b) =>
        Assert.Equal(SaveList.Normalize(a), SaveList.Normalize(b));

    [Fact]
    public void LIB_06_games_are_found_by_install_folder_title_and_alias()
    {
        var list = SaveListParser.Parse(new StringReader(Manifest), "test", DateTime.UtcNow);

        Assert.Equal("Black Myth: Wukong", Assert.Single(list.ByInstallDir("blackmythwukong")).Title);
        Assert.Equal("Black Myth: Wukong", list.ByTitle("Black Myth Wukong")!.Title);
        Assert.Equal("Terraria", list.ByTitle("old name of terraria")!.Title);
    }

    [Fact]
    public void The_saved_index_reads_back_the_same()
    {
        using var world = new TestWorld();
        var store = new SaveListStore(world.Root);
        var manifest = Path.Combine(world.Root, "manifest.yaml");
        File.WriteAllText(manifest, Manifest);

        var written = store.UpdateFromFile(manifest);
        var read = store.Load()!;

        Assert.Equal(written.Games.Select(g => g.Title), read.Games.Select(g => g.Title));
        Assert.Equal("Terraria", read.BySteamId(105600)!.Title);
        Assert.Equal(written.Games.Sum(g => g.Files.Count), read.Games.Sum(g => g.Files.Count));
    }

    [Fact]
    public async Task Offline_the_first_time_Ludusavis_copy_is_used_and_after_that_the_saved_one()
    {
        using var world = new TestWorld();
        var manifest = Path.Combine(world.Root, "manifest.yaml");
        File.WriteAllText(manifest, Manifest);
        using var offline = new HttpClient(new NoNetwork());
        var store = new SaveListStore(Path.Combine(world.Root, "data"), offline);

        var (first, why) = await store.GetAsync(refresh: false, manifest, CancellationToken.None);
        Assert.Equal("Terraria", first!.BySteamId(105600)!.Title);
        Assert.Contains("Ludusavi's copy", why, StringComparison.Ordinal);

        var (later, whyLater) = await store.GetAsync(refresh: true, fallbackManifest: null, CancellationToken.None);
        Assert.Equal("Terraria", later!.BySteamId(105600)!.Title);
        Assert.Contains("Couldn't refresh the save list", whyLater, StringComparison.Ordinal);

        var (none, whyNone) = await new SaveListStore(Path.Combine(world.Root, "empty"), offline).GetAsync(false, null, CancellationToken.None);
        Assert.Null(none);
        Assert.Contains("Engine rules and the name search still ran", whyNone, StringComparison.Ordinal);
    }

    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("No internet connection.");
    }

    /// <summary>Reads the real list from a Ludusavi install when there is one: about 53,000 titles.</summary>
    [Fact]
    public void FIND_01_the_real_save_list_reads_quickly_and_finds_known_games()
    {
        var real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ludusavi", "manifest.yaml");
        if (!File.Exists(real))
        {
            output.WriteLine("No Ludusavi manifest on this PC; nothing to check.");
            return;
        }

        using var world = new TestWorld();
        var clock = Stopwatch.StartNew();
        var list = new SaveListStore(world.Root).UpdateFromFile(real);
        var parsed = clock.Elapsed;
        clock.Restart();
        var reloaded = new SaveListStore(world.Root).Load()!;
        var loaded = clock.Elapsed;

        output.WriteLine($"{list.Games.Count} games, {list.Games.Count(g => g.Files.Count > 0)} with Windows paths; " +
            $"parsed in {parsed.TotalSeconds:0.0} s, reloaded in {loaded.TotalSeconds:0.00} s, index {new FileInfo(Path.Combine(world.Root, "savelist.json.gz")).Length / 1024} KB");
        Assert.Equal("Terraria", reloaded.BySteamId(105600)!.Title);
        Assert.Equal("Black Myth: Wukong", reloaded.BySteamId(2358720)!.Title);
        Assert.Equal("Sekiro: Shadows Die Twice", reloaded.BySteamId(814380)!.Title);
        Assert.Contains(reloaded.BySteamId(814380)!.Files, f => f.Path == "<winAppData>/Sekiro/<storeUserId>/S0000.sl2");
    }
}
