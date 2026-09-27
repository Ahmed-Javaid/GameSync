using System.Net;
using System.Text;
using GameSync.Core.Art;
using GameSync.Core.Discovery;

namespace GameSync.Core.Tests;

/// <summary>ART-01 to ART-08: Steam's own art by app ID, checked, cached, and refreshed only when Steam's changes.</summary>
public class ArtTests
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, (byte)'I', (byte)'H', (byte)'D', (byte)'R'];
    private static readonly byte[] Program = [(byte)'M', (byte)'Z', 0x90, 0, 3, 0, 0, 0, 4, 0, 0, 0, 0xFF, 0xFF, 0, 0];

    [Fact]
    public void ART_02_image_names_come_from_the_store_api_hashed_paths_included()
    {
        var assets = SteamArt.ParseAssets(Reply((2868840, 1787169309, true), (814380, 1762888662, false)));

        var slay = Assert.Single(assets, a => a.AppId == 2868840);
        Assert.Equal("https://shared.steamstatic.com/store_item_assets/steam/apps/2868840/4bf4dad2/library_600x900_2x.jpg?t=1787169309",
            SteamArt.ImageUrl(slay, ArtKind.Cover)!.ToString());
        Assert.Equal("https://shared.steamstatic.com/store_item_assets/steam/apps/2868840/1426/library_hero.jpg?t=1787169309",
            SteamArt.ImageUrl(slay, ArtKind.Hero)!.ToString());
        Assert.Equal("https://shared.steamstatic.com/store_item_assets/steam/apps/2868840/logo.png", SteamArt.ImageUrl(slay, ArtKind.Logo)!.ToString());
        Assert.Equal("https://shared.steamstatic.com/store_item_assets/steam/apps/814380/library_600x900_2x.jpg?t=1762888662",
            SteamArt.ImageUrl(assets.Single(a => a.AppId == 814380), ArtKind.Cover)!.ToString());
    }

    [Fact]
    public void ART_08_what_Steam_sends_is_checked_before_it_becomes_an_address()
    {
        const string hostile = """
            {"response":{"store_items":[
              {"appid":1,"success":1,"assets":{"asset_url_format":"https://evil.example/${FILENAME}","library_capsule":"a.jpg"}},
              {"appid":2,"success":1,"assets":{"asset_url_format":"steam/apps/2/${FILENAME}","library_capsule_2x":"../../x.jpg","library_hero":"ok/library_hero.jpg"}},
              {"appid":3,"success":2}]}}
            """;

        var assets = SteamArt.ParseAssets(hostile);

        var two = Assert.Single(assets);
        Assert.Equal(2, two.AppId);
        Assert.Null(SteamArt.ImageUrl(two, ArtKind.Cover));
        Assert.StartsWith(SteamArt.ImageBase, SteamArt.ImageUrl(two, ArtKind.Hero)!.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("jpeg", ".jpg")]
    [InlineData("png", ".png")]
    [InlineData("webp", ".webp")]
    [InlineData("program", null)]
    [InlineData("text", null)]
    public void ART_08_only_JPEG_PNG_or_WebP_by_content(string kind, string? expected)
    {
        var content = kind switch
        {
            "jpeg" => Jpeg,
            "png" => Png,
            "webp" => [.. "RIFF"u8, 0, 0, 0, 0, .. "WEBPVP8 "u8],
            "program" => Program,
            _ => Encoding.UTF8.GetBytes("<html>not an image</html>"),
        };

        Assert.Equal(expected, ArtCheck.ExtensionOf(content));
    }

    [Fact]
    public async Task ART_07_art_is_cached_and_a_program_posing_as_a_cover_is_refused()
    {
        using var world = new TestWorld();
        var steam = new FakeSteam();
        steam.Apps[105600] = 1769844435;
        steam.Apps[666] = 1;
        steam.Images["/105600/"] = Jpeg;
        steam.Images["/666/"] = Program;
        using var cache = new ArtCache(world.Root, steam);

        var refresh = await cache.RefreshAsync([105600, 666], CancellationToken.None);

        Assert.Equal(3, refresh.Downloaded);
        Assert.EndsWith(".jpg", cache.Find(105600, ArtKind.Cover), StringComparison.Ordinal);
        Assert.NotNull(cache.Find(105600, ArtKind.Hero));
        Assert.Null(cache.Find(666, ArtKind.Cover));
        Assert.Empty(Directory.EnumerateFiles(cache.Folder, "*.part", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ART_07_art_from_Steams_store_is_checked_monthly_and_comes_down_again_only_when_it_changed()
    {
        using var world = new TestWorld();
        var now = new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
        var steam = new FakeSteam();
        steam.Apps[105600] = 100;
        steam.Images["/105600/"] = Jpeg;
        using var cache = new ArtCache(world.Root, steam, () => now);
        await cache.RefreshAsync([105600], CancellationToken.None);
        var asked = steam.ApiCalls;

        now = now.AddDays(10);
        await cache.RefreshAsync([105600], CancellationToken.None);
        Assert.Equal(asked, steam.ApiCalls);

        now = now.AddDays(25);
        var images = steam.ImageCalls;
        Assert.Equal(0, (await cache.RefreshAsync([105600], CancellationToken.None)).Downloaded);
        Assert.Equal(asked + 1, steam.ApiCalls);
        Assert.Equal(images, steam.ImageCalls);

        now = now.AddDays(31);
        steam.Apps[105600] = 200;
        Assert.Equal(3, (await cache.RefreshAsync([105600], CancellationToken.None)).Downloaded);
    }

    [Fact]
    public async Task ART_07_art_comes_from_the_Steam_clients_own_cache_and_Steams_store_is_asked_once()
    {
        using var world = new TestWorld();
        var now = new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
        var steamRoot = Path.Combine(world.Root, "Steam");
        SteamClientArt(steamRoot, "105600", ("library_600x900.jpg", Jpeg), ("library_hero.jpg", Jpeg), ("logo.png", Png));
        SteamClientArt(steamRoot, @"2868840\4bf4dad2", ("library_capsule.jpg", Jpeg));
        var steam = new FakeSteam();
        steam.Apps[105600] = 100;
        steam.Apps[2868840] = 100;
        using var cache = new ArtCache(world.Root, steam, () => now, steamRoot);

        var first = await cache.RefreshAsync([105600, 2868840], CancellationToken.None);

        Assert.Equal((4, 2, 0), (first.Copied, first.Asked, first.Downloaded));
        Assert.NotNull(cache.Find(105600, ArtKind.Logo));
        Assert.NotNull(cache.Find(2868840, ArtKind.Cover));

        now = now.AddDays(60);
        var later = await cache.RefreshAsync([105600, 2868840], CancellationToken.None);
        Assert.Equal((0, 0), (later.Copied, later.Asked));
        Assert.Equal(1, steam.ApiCalls);
    }

    [Fact]
    public async Task ART_08_a_program_in_the_Steam_clients_cache_is_refused_and_a_newer_cover_there_is_taken()
    {
        using var world = new TestWorld();
        var steamRoot = Path.Combine(world.Root, "Steam");
        SteamClientArt(steamRoot, "666", ("library_600x900.jpg", Program));
        var cover = SteamClientArt(steamRoot, "105600", ("library_600x900.jpg", Jpeg));
        var steam = new FakeSteam();
        using var cache = new ArtCache(world.Root, steam, steamRoot: steamRoot);
        await cache.RefreshAsync([666, 105600], CancellationToken.None);
        Assert.Null(cache.Find(666, ArtKind.Cover));

        File.SetLastWriteTimeUtc(cover, DateTime.UtcNow.AddHours(1));
        Assert.Equal(1, (await cache.RefreshAsync([105600], CancellationToken.None)).Copied);
    }

    [Fact]
    public async Task ART_05_an_app_Steam_doesnt_know_is_asked_about_again_after_a_week()
    {
        using var world = new TestWorld();
        var now = new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
        var steam = new FakeSteam();
        using var cache = new ArtCache(world.Root, steam, () => now);

        await cache.RefreshAsync([424242], CancellationToken.None);
        now = now.AddDays(3);
        await cache.RefreshAsync([424242], CancellationToken.None);
        Assert.Equal(1, steam.ApiCalls);

        now = now.AddDays(5);
        await cache.RefreshAsync([424242], CancellationToken.None);
        Assert.Equal(2, steam.ApiCalls);
        Assert.Null(cache.Find(424242, ArtKind.Cover));
    }

    [Fact]
    public async Task ART_07_offline_leaves_the_cache_as_it_is()
    {
        using var world = new TestWorld();
        var steam = new FakeSteam();
        steam.Apps[105600] = 100;
        steam.Images["/105600/"] = Jpeg;
        using (var cache = new ArtCache(world.Root, steam))
        {
            await cache.RefreshAsync([105600], CancellationToken.None);
        }

        steam.Offline = true;
        using var later = new ArtCache(world.Root, steam, () => DateTime.UtcNow.AddDays(3));
        Assert.Equal(0, (await later.RefreshAsync([105600, 7], CancellationToken.None)).Downloaded);
        Assert.NotNull(later.Find(105600, ArtKind.Cover));
    }

    [Fact]
    public async Task Software_and_demos_of_software_arent_games_but_a_games_demo_is()
    {
        using var world = new TestWorld();
        var steam = new Replies(url =>
        {
            var asked = Uri.UnescapeDataString(url);
            if (asked.Contains("\"include_assets\":false", StringComparison.Ordinal))
            {
                return Json("""{"response":{"store_items":[{"appid":223850,"type":6},{"appid":892970,"type":0}]}}""");
            }

            if (asked.StartsWith(SteamArt.StoreApi, StringComparison.Ordinal))
            {
                static string Item(long id, int type, string? parent) =>
                    $"{{\"appid\":{id},\"success\":1,\"type\":{type},{(parent is null ? "" : $"\"related_items\":{{\"parent_appid\":{parent}}},")}" +
                    $"\"assets\":{{\"asset_url_format\":\"steam/apps/{id}/${{FILENAME}}?t=1\",\"library_capsule\":\"library_600x900.jpg\",\"last_modified\":1}}}}";
                return Json("{\"response\":{\"store_items\":[" + string.Join(",", Item(431960, 6, null), Item(231350, 1, "223850"), Item(1234, 1, "892970"), Item(105600, 0, null)) + "]}}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var cache = new ArtCache(world.Root, steam);

        await cache.RefreshAsync([431960, 231350, 1234, 105600], CancellationToken.None);

        Assert.True(cache.IsSoftware(431960), "Wallpaper Engine is software.");
        Assert.True(cache.IsSoftware(231350), "3DMark Demo is the demo of software.");
        Assert.False(cache.IsSoftware(1234), "A game's demo is a game.");
        Assert.False(cache.IsSoftware(105600));
    }

    [Fact]
    public void PLAY_09_Steams_own_play_record_adds_up_across_accounts()
    {
        using var world = new TestWorld();
        var steam = Path.Combine(world.Root, "Steam");
        LocalConfig(steam, "111", ("105600", 1759000000, 600), ("814380", 1759500000, 3660));
        LocalConfig(steam, "222", ("105600", 1759200000, 120), ("480", 0, 0));

        var plays = SteamActivity.Read(steam);

        Assert.Equal(2, plays.Count);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1759200000).UtcDateTime, plays[105600].LastPlayedUtc);
        Assert.Equal(TimeSpan.FromMinutes(720), plays[105600].Playtime);
        Assert.Equal(TimeSpan.FromMinutes(3660), plays[814380].Playtime);
    }

    /// <summary>Art the Steam client cached for an app, under <c>appcache\librarycache\&lt;folder&gt;</c>; returns the last file's path.</summary>
    private static string SteamClientArt(string steamRoot, string folder, params (string Name, byte[] Content)[] files)
    {
        var path = Directory.CreateDirectory(Path.Combine(steamRoot, "appcache", "librarycache", folder)).FullName;
        var last = "";
        foreach (var (name, content) in files)
        {
            last = Path.Combine(path, name);
            File.WriteAllBytes(last, content);
        }

        return last;
    }

    private static void LocalConfig(string steamRoot, string account, params (string App, long LastPlayed, long Minutes)[] apps)
    {
        var folder = Directory.CreateDirectory(Path.Combine(steamRoot, "userdata", account, "config")).FullName;
        var body = string.Concat(apps.Select(a => $"\t\t\t\t\t\"{a.App}\"\n\t\t\t\t\t{{\n\t\t\t\t\t\t\"LastPlayed\"\t\t\"{a.LastPlayed}\"\n\t\t\t\t\t\t\"Playtime\"\t\t\"{a.Minutes}\"\n\t\t\t\t\t}}\n"));
        File.WriteAllText(Path.Combine(folder, "localconfig.vdf"),
            "\"UserLocalConfigStore\"\n{\n\t\"Software\"\n\t{\n\t\t\"Valve\"\n\t\t{\n\t\t\t\"Steam\"\n\t\t\t{\n\t\t\t\t\"apps\"\n\t\t\t\t{\n" + body + "\t\t\t\t}\n\t\t\t}\n\t\t}\n\t}\n}\n");
    }

    private static string Reply(params (long App, long Modified, bool Hashed)[] apps) =>
        "{\"response\":{\"store_items\":[" + string.Join(",", apps.Select(a =>
            $"{{\"appid\":{a.App},\"success\":1,\"assets\":{{\"asset_url_format\":\"steam/apps/{a.App}/${{FILENAME}}?t={a.Modified}\"," +
            (a.Hashed
                ? "\"library_capsule\":\"4bf4dad2/library_600x900.jpg\",\"library_capsule_2x\":\"4bf4dad2/library_600x900_2x.jpg\",\"library_hero\":\"1426/library_hero.jpg\","
                : "\"library_capsule\":\"library_600x900.jpg\",\"library_capsule_2x\":\"library_600x900_2x.jpg\",\"library_hero\":\"library_hero.jpg\",") +
            $"\"last_modified\":{a.Modified}}}}}")) + "]}}";

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };

    /// <summary>Answers each request with what a function gives for its address.</summary>
    private sealed class Replies(Func<string, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(answer(request.RequestUri!.ToString()));
    }

    /// <summary>Stands in for Steam's store API and image server.</summary>
    private sealed class FakeSteam : HttpMessageHandler
    {
        public Dictionary<long, long> Apps { get; } = [];

        public Dictionary<string, byte[]> Images { get; } = [];

        public bool Offline { get; set; }

        public int ApiCalls { get; private set; }

        public int ImageCalls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Offline)
            {
                throw new HttpRequestException("No network.");
            }

            var url = request.RequestUri!.ToString();
            if (url.StartsWith(SteamArt.StoreApi, StringComparison.Ordinal))
            {
                ApiCalls++;
                var asked = Apps.Keys.Where(id => Uri.UnescapeDataString(url).Contains($"\"appid\":{id}", StringComparison.Ordinal)).ToArray();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Reply(asked.Select(id => (id, Apps[id], false)).ToArray())) });
            }

            ImageCalls++;
            var image = Images.FirstOrDefault(i => url.Contains(i.Key, StringComparison.Ordinal)).Value;
            return Task.FromResult(image is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(image) });
        }
    }
}
