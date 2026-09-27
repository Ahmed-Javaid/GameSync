using System.Text.Json;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.Scanning;
using GameSync.Core.State;
using GameSync.Core.Sync;

namespace GameSync.Core.Tests;

/// <summary>FIND-10 and R7: registry saves round-trip between PCs, and are only ever written under the game's own key.</summary>
public class RegistryTests
{
    private const string PlayerPrefs = "HKEY_CURRENT_USER/Software/Studio MDHR/Cuphead";
    private const string Unrelated = "HKEY_CURRENT_USER/Software/Other Company/Other Game";

    private static readonly GameDefinition Cuphead = new()
    {
        Id = GameId.Parse("cuphead"),
        Title = "Cuphead",
        Roots = new Dictionary<string, string> { ["saves"] = "<roaming>/Cuphead" },
        Rules = [new SaveRule { Root = "saves" }],
        Registry = [new RegistryRule { Key = PlayerPrefs }],
    };

    [Fact]
    public void An_export_names_its_key_and_reads_back_the_same_whatever_order_values_came_in()
    {
        var node = Prefs(("unlocks", "7"));
        node.Values["Screenmanager Resolution Width_h182942802"] = new RegistryValue("DWord", "1920");
        var bytes = RegistryFile.Write(PlayerPrefs, node);

        var (key, read) = RegistryFile.Read(bytes);

        Assert.Equal(PlayerPrefs, key);
        Assert.Equal(bytes, RegistryFile.Write(key, read));
        Assert.Equal("hkey-current-user-software-studio-mdhr-cuphead.json", RegistryFile.FileName(PlayerPrefs));
        Assert.Throws<FormatException>(() => RegistryFile.Read("{ \"values\": {} }"u8.ToArray()));
    }

    [Fact]
    public async Task FIND_10_PlayerPrefs_round_trip_and_nothing_else_in_the_registry_changes()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        desktop.AddResolvedGame(Cuphead, desktop.KnownFolders);
        laptop.AddResolvedGame(Cuphead, laptop.KnownFolders);
        var prefs = Prefs(("unlocks", "7"));
        prefs.Keys["Controls"] = Prefs(("jump", "space"));
        desktop.Registry.Set(PlayerPrefs, prefs, DateTime.UtcNow.AddMinutes(-5));
        laptop.Registry.Set(Unrelated, Prefs(("volume", "3")), DateTime.UtcNow.AddDays(-9));
        desktop.Played("cuphead");

        var uploaded = Assert.Single(await desktop.SyncAsync());
        var result = Assert.Single(await laptop.SyncAsync());

        Assert.Equal(SyncAction.Upload, uploaded.Action);
        Assert.Contains(Assert.Single(await Cloud.VersionsAsync(world, "cuphead")).Files,
            f => f.Path == $"registry/{RegistryFile.FileName(PlayerPrefs)}");
        Assert.Equal(SyncAction.Download, result.Action);
        Assert.Equal(RegistryFile.Write(PlayerPrefs, prefs), RegistryFile.Write(PlayerPrefs, laptop.Registry.Get(PlayerPrefs)!));
        Assert.Equal([PlayerPrefs], laptop.Registry.Written);
        Assert.Equal("3", laptop.Registry.Get(Unrelated)!.Values["volume"].Data);
    }

    [Fact]
    public async Task FIND_10_a_change_made_during_play_uploads_and_a_key_that_vanishes_is_not_a_deletion()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        desktop.AddResolvedGame(Cuphead, desktop.KnownFolders);
        desktop.Registry.Set(PlayerPrefs, Prefs(("unlocks", "7")), DateTime.UtcNow.AddDays(-3));
        await desktop.SyncAsync();

        desktop.Played("cuphead");
        desktop.Registry.Set(PlayerPrefs, Prefs(("unlocks", "8")), DateTime.UtcNow.AddMinutes(-10));
        var played = Assert.Single(await desktop.SyncAsync());

        desktop.Registry.Remove(PlayerPrefs);
        var gone = Assert.Single(await desktop.SyncAsync());

        Assert.Equal(SyncAction.Upload, played.Action);
        Assert.Equal(VersionKind.Normal, (await Cloud.VersionsAsync(world, "cuphead")).MaxBy(v => v.CreatedUtc)!.Kind);
        Assert.Equal(SyncAction.None, gone.Action);
        Assert.Equal(2, (await Cloud.VersionsAsync(world, "cuphead")).Count);
    }

    [Fact]
    public async Task R7_an_export_that_would_write_another_key_is_blocked_and_the_registry_is_untouched()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        desktop.AddResolvedGame(Cuphead, desktop.KnownFolders);
        laptop.AddResolvedGame(Cuphead, laptop.KnownFolders);
        desktop.Registry.Set(PlayerPrefs, Prefs(("unlocks", "7")), DateTime.UtcNow.AddMinutes(-5));
        desktop.Played("cuphead");
        await desktop.SyncAsync();
        var real = Assert.Single(await Cloud.VersionsAsync(world, "cuphead"));

        // A tampered cloud: the export behind Cuphead's file name aims at Windows' startup key.
        var evil = RegistryFile.Write("HKEY_CURRENT_USER/Software/Microsoft/Windows/CurrentVersion/Run", Prefs(("updater", "C:\\evil.exe")));
        var hash = BlobId.FromHash(System.Security.Cryptography.SHA256.HashData(evil));
        await world.CloudFor(null).Blobs.PutAsync(GameId.Parse("cuphead"), hash, new MemoryStream(evil), CancellationToken.None);
        var crafted = real with
        {
            Id = VersionId.Parse("crafted"),
            Parent = real.Id,
            Device = new DeviceInfo(DeviceId.Parse("d-evil"), "EVIL"),
            CreatedUtc = DateTime.UtcNow,
            Files = real.Files.Select(f => f.Path.StartsWith("registry/", StringComparison.Ordinal) ? f with { Hash = hash, Size = evil.Length } : f).ToList(),
        };
        await File.WriteAllBytesAsync(Path.Combine(Cloud.VersionsFolder(world.Cloud, "cuphead"), "crafted.json"), JsonSerializer.SerializeToUtf8Bytes(crafted, Json.Options));

        var result = Assert.Single(await laptop.SyncAsync());

        Assert.Equal(GameStatus.Blocked, result.Status);
        Assert.Empty(laptop.Registry.Written);
        Assert.Empty(laptop.Registry.Keys);
    }

    /// <summary>The real registry, in a key of the test's own that it removes afterwards; no game's key is touched.</summary>
    [Fact]
    public void FIND_10_Windows_exports_every_kind_of_value_and_writes_back_exactly_that()
    {
        var key = $"HKEY_CURRENT_USER/Software/GameSync Tests/{Guid.NewGuid():N}";
        var subKey = key["HKEY_CURRENT_USER/".Length..].Replace('/', '\\');
        try
        {
            using (var created = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(subKey))
            {
                created.SetValue("text", "hello");
                created.SetValue("path", "%TEMP%\\x", Microsoft.Win32.RegistryValueKind.ExpandString);
                created.SetValue("lines", new[] { "a", "b" });
                created.SetValue("dword", unchecked((int)0xFFFFFFF0), Microsoft.Win32.RegistryValueKind.DWord);
                created.SetValue("qword", long.MaxValue, Microsoft.Win32.RegistryValueKind.QWord);
                created.SetValue("blob", new byte[] { 0, 1, 2, 255 });
                created.SetValue("", "the default value");
                using var child = created.CreateSubKey("Slot 1");
                child.SetValue("level", 12);
            }

            // Unity keeps float PlayerPrefs as a REG_DWORD holding 8 bytes, which only the raw form keeps exactly.
            SetRaw(subKey, "speed_h123", 4, BitConverter.GetBytes(1.5d));

            var registry = new Windows.WindowsRegistry();
            var (node, changed) = registry.Export(key)!.Value;
            Assert.InRange(changed, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(1));
            Assert.Equal(("ExpandString", "%TEMP%\\x"), (node.Values["path"].Kind, node.Values["path"].Data));
            Assert.Equal(("DWord", "4294967280"), (node.Values["dword"].Kind, node.Values["dword"].Data));
            Assert.Equal(("Raw", 4), (node.Values["speed_h123"].Kind, node.Values["speed_h123"].Type));
            Assert.Equal(["a", "b"], node.Values["lines"].Lines!);

            // Change it, then write the export back: exactly the export again, extra values and keys gone.
            using (var changedKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subKey, writable: true)!)
            {
                changedKey.SetValue("text", "changed");
                changedKey.SetValue("extra", 1);
                changedKey.CreateSubKey("Extra Key").Dispose();
            }

            registry.Import(key, node);
            Assert.Equal(RegistryFile.Write(key, node), RegistryFile.Write(key, registry.Export(key)!.Value.Node));

            // A bad export changes nothing.
            var bad = RegistryFile.Read(RegistryFile.Write(key, node)).Node;
            bad.Values["text"] = new RegistryValue("String", "not written");
            bad.Values["dword"] = new RegistryValue("DWord", "not a number");
            Assert.Throws<FormatException>(() => registry.Import(key, bad));
            Assert.Equal("hello", registry.Export(key)!.Value.Node.Values["text"].Data);
        }
        finally
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
            using var parent = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\GameSync Tests");
            if (parent is { SubKeyCount: 0, ValueCount: 0 })
            {
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKey(@"Software\GameSync Tests", throwOnMissingSubKey: false);
            }
        }
    }

    private static void SetRaw(string subKey, string name, int type, byte[] data)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subKey, writable: true)!;
        Assert.Equal(0, RegSetValueEx(key.Handle, name, 0, type, data, data.Length));
    }

    [System.Runtime.InteropServices.DllImport("advapi32.dll", EntryPoint = "RegSetValueExW", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int RegSetValueEx(Microsoft.Win32.SafeHandles.SafeRegistryHandle key, string name, int reserved, int type, byte[] data, int size);

    private static RegistryNode Prefs(params (string Name, string Data)[] values)
    {
        var node = new RegistryNode();
        foreach (var (name, data) in values)
        {
            node.Values[name] = new RegistryValue("String", data);
        }

        return node;
    }
}
