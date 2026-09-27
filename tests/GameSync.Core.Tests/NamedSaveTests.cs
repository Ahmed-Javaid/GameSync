using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Core.Sync;

namespace GameSync.Core.Tests;

/// <summary>Named saves (BAK-18) and importing the save folders kept by hand (BAK-19), laid out like Bloodborne on shadPS4.</summary>
public class NamedSaveTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly GameId Bloodborne = GameId.Parse("bloodborne");

    [Fact]
    public async Task BAK_18_a_named_save_comes_back_after_the_boss_fight()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        var live = AddBloodborne(desktop);
        Write(live, "userdata0000", "at the Lady Maria lamp");
        await desktop.SyncAsync();

        var named = await desktop.Service().SaveAsAsync(Bloodborne, "Before Lady Maria", Ct);
        Assert.Equal(SyncAction.None, named.Action);

        desktop.Played("bloodborne");
        Write(live, "userdata0000", "after the fight, Maria dead");
        await desktop.SyncAsync();

        var restored = await desktop.Service().RestoreNamedAsync(Bloodborne, "before lady maria", Ct);

        Assert.Contains("'Before Lady Maria'", restored.Message);
        Assert.Equal("at the Lady Maria lamp", Read(live, "userdata0000"));
        var history = await desktop.Service().HistoryAsync(Bloodborne, Ct);
        Assert.Contains(history, h => h.Version.Files.Any(f => f.Hash == Hash("after the fight, Maria dead")));
        Assert.Equal("Before Lady Maria", Assert.Single(await desktop.Service().NamedSavesAsync(Bloodborne, Ct)).Name);
    }

    [Fact]
    public async Task BAK_18_saving_with_a_name_makes_the_current_save_and_the_laptop_sees_the_name()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        var live = AddBloodborne(desktop);
        AddBloodborne(laptop);
        Write(live, "userdata0000", "start");
        await desktop.SyncAsync();
        await laptop.SyncAsync();

        // Changed without a recorded session, which a sync would hold for review; naming it is the user's say-so.
        Write(live, "userdata0000", "at the Orphan lamp");
        var named = await desktop.Service().SaveAsAsync(Bloodborne, "Before Orphan", Ct);

        Assert.Equal(SyncAction.Upload, named.Action);
        Assert.Equal(GameStatus.Synced, named.Status);
        Assert.Equal(SyncAction.Download, Assert.Single(await laptop.SyncAsync()).Action);
        Assert.Equal("at the Orphan lamp", Read(laptop.Folder("bloodborne"), "userdata0000"));
        var onLaptop = Assert.Single(await laptop.Service().NamedSavesAsync(Bloodborne, Ct));
        Assert.Equal(("Before Orphan", "DESKTOP"), (onLaptop.Name, onLaptop.DeviceName));
    }

    [Fact]
    public async Task BAK_18_when_the_other_pc_changed_the_game_too_the_named_save_is_set_aside()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        var live = AddBloodborne(desktop);
        AddBloodborne(laptop);
        Write(live, "userdata0000", "start");
        await desktop.SyncAsync();
        await laptop.SyncAsync();
        laptop.Played("bloodborne");
        Write(laptop.Folder("bloodborne"), "userdata0000", "laptop run");
        await laptop.SyncAsync();

        Write(live, "userdata0000", "desktop run");
        await desktop.Service().SaveAsAsync(Bloodborne, "Desktop's run", Ct);

        // Set aside with its name; this PC's agreed save is still the one both PCs started from.
        var save = Assert.Single(await desktop.Service().NamedSavesAsync(Bloodborne, Ct));
        Assert.Equal(VersionKind.Kept, save.Version.Kind);
        Assert.Equal("desktop run", Read(live, "userdata0000"));
        Assert.Equal(Hash("start"), desktop.State.GetState(Bloodborne).Base!.Files.Single().Hash);
    }

    [Fact]
    public async Task BAK_18_a_name_can_be_changed_or_removed_and_the_save_stays_in_history()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        var live = AddBloodborne(desktop);
        Write(live, "userdata0000", "Djura dead");
        await desktop.SyncAsync();
        var service = desktop.Service();
        await service.SaveAsAsync(Bloodborne, "Djarn dead w emblem", Ct);

        await service.RenameSaveAsync(Bloodborne, "djarn", "Djura dead, with the emblem", Ct);
        Assert.Equal("Djura dead, with the emblem", Assert.Single(await service.NamedSavesAsync(Bloodborne, Ct)).Name);

        await service.ForgetSaveAsync(Bloodborne, "Djura dead, with the emblem", Ct);
        Assert.Empty(await service.NamedSavesAsync(Bloodborne, Ct));
        Assert.Single(await service.HistoryAsync(Bloodborne, Ct));
    }

    [Fact]
    public async Task BAK_18_named_saves_are_never_thinned()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        var live = AddBloodborne(desktop);
        Write(live, "userdata0000", "before the Moon Presence");
        await desktop.SyncAsync();
        await desktop.Service().SaveAsAsync(Bloodborne, "Before Moon Presence", Ct);
        for (var i = 0; i < 5; i++)
        {
            desktop.Played("bloodborne");
            Write(live, "userdata0000", $"NG+ progress {i}");
            await desktop.SyncAsync();
        }

        var preview = await desktop.Service().PreviewThinAsync(Bloodborne, keepNewest: 1, Ct);

        var named = Assert.Single(await desktop.Service().NamedSavesAsync(Bloodborne, Ct));
        Assert.NotEmpty(preview.Versions);
        Assert.DoesNotContain(preview.Versions, v => v.Id == named.Version.Id);
    }

    [Fact]
    public async Task BAK_19_kept_folders_become_named_saves_and_nothing_on_disk_changes()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        var cusa = Path.Combine(world.Root, "DESKTOP", "Shadlix", "user", "savedata", "1", "CUSA00207");
        var live = AddBloodborne(desktop, Path.Combine(cusa, "SPRJ0005"));
        AddBloodborne(laptop);
        Write(live, "userdata0000", "live");
        Write(live, "sce_sys/param.sfo", "param");
        await desktop.SyncAsync();

        var t = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        Write(Path.Combine(cusa, "Before Orphan", "SPRJ0005"), "userdata0000", "orphan", t);
        Write(Path.Combine(cusa, "Before Orphan", "SPRJ0005"), "sce_sys/param.sfo", "param", t);
        Write(Path.Combine(cusa, "Before Orphan", "SPRJ0005"), "tool.exe", "not a save", t);
        Write(Path.Combine(cusa, "After maria", "SPRJ0005"), "userdata0000", "after maria", t.AddDays(1));
        Write(Path.Combine(cusa, "SPRJ0005 - Copy"), "userdata0000", "a plain copy", t.AddDays(2));
        Write(Path.Combine(cusa, "Before sus", "SPRJ0005"), "userdata0000", "sus", t.AddDays(3));
        Write(Path.Combine(cusa, "Before sus", "ded beast", "SPRJ0005"), "userdata0000", "ded beast", t.AddDays(4));
        Write(Path.Combine(cusa, "Castle", "SPRJ0005"), "userdata0000", "castle", t.AddDays(5));
        Write(Path.Combine(cusa, "Castle"), "SPRJ0005.rar", "a rar archive", t);
        Write(Path.Combine(cusa, "Reshade"), "shader.fx", "not a save", t);
        Zip(Path.Combine(cusa, "Before Crow.zip"), ("Before Crow/SPRJ0005/userdata0000", "crow"), ("Before Crow/SPRJ0005/sce_sys/param.sfo", "param"));
        Zip(Path.Combine(cusa, "Evil.zip"), ("Evil/SPRJ0005/userdata0000", "evil"), ("Evil/SPRJ0005/../../../../escaped.sav", "escaped"));
        var before = Tree(cusa);

        var preview = await desktop.Service().ImportSavesAsync(Bloodborne, cusa, apply: false, null, Ct);
        var expected = new[] { "After maria", "Before Crow", "Before Orphan", "Before sus", "Before sus / ded beast", "Castle", "SPRJ0005 - Copy" };
        Assert.Equal(expected, preview.Items.Select(i => i.Name).Order());
        Assert.Contains(preview.Skipped, s => s.Contains("SPRJ0005.rar", StringComparison.Ordinal));
        Assert.Contains(preview.Skipped, s => s.StartsWith("Reshade", StringComparison.Ordinal));
        Assert.Contains(preview.Skipped, s => s.StartsWith("Evil.zip", StringComparison.Ordinal) && s.Contains("unsafe", StringComparison.Ordinal));
        Assert.Contains(preview.Skipped, s => s.Contains("tool.exe", StringComparison.Ordinal));
        Assert.Empty(await desktop.Service().NamedSavesAsync(Bloodborne, Ct));

        var report = await desktop.Service().ImportSavesAsync(Bloodborne, cusa, apply: true, null, Ct);

        Assert.Equal(7, report.Added);
        Assert.Equal(before, Tree(cusa));
        Assert.False(File.Exists(Path.Combine(world.Root, "DESKTOP", "Shadlix", "escaped.sav")));
        Assert.Equal("live", Read(live, "userdata0000"));
        var saves = await desktop.Service().NamedSavesAsync(Bloodborne, Ct);
        Assert.Equal(expected, saves.Select(s => s.Name).Order());
        Assert.All(saves, s => Assert.Equal((VersionKind.Kept, VersionOrigin.Imported), (s.Version.Kind, s.Version.Origin)));
        Assert.Equal(t, saves.Single(s => s.Name == "Before Orphan").SavedUtc);

        // The laptop sees them too, and restoring one brings back exactly those files.
        await laptop.SyncAsync();
        await laptop.Service().RestoreNamedAsync(Bloodborne, "Before Orphan", Ct);
        Assert.Equal("orphan", Read(laptop.Folder("bloodborne"), "userdata0000"));
        Assert.Equal("param", Read(laptop.Folder("bloodborne"), "sce_sys/param.sfo"));
        Assert.False(File.Exists(Path.Combine(laptop.Folder("bloodborne"), "tool.exe")));

        // Importing again adds nothing.
        Assert.Equal(0, (await desktop.Service().ImportSavesAsync(Bloodborne, cusa, apply: true, null, Ct)).Added);
    }

    [Fact]
    public async Task BAK_19_a_kept_folder_already_in_history_just_gets_the_name()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        var cusa = Path.Combine(world.Root, "DESKTOP", "CUSA00207");
        var live = AddBloodborne(desktop, Path.Combine(cusa, "SPRJ0005"));
        Write(live, "userdata0000", "same as the live save");
        await desktop.SyncAsync();
        Write(Path.Combine(cusa, "Snapshot", "SPRJ0005"), "userdata0000", "same as the live save", File.GetLastWriteTimeUtc(Path.Combine(live, "userdata0000")));

        var report = await desktop.Service().ImportSavesAsync(Bloodborne, cusa, apply: true, null, Ct);

        Assert.Equal(0, report.Added);
        Assert.Equal("Snapshot", Assert.Single(await desktop.Service().NamedSavesAsync(Bloodborne, Ct)).Name);
        Assert.Single(await Cloud.VersionsAsync(world, "bloodborne"));
    }

    /// <summary>Bloodborne on shadPS4: the live save is the SPRJ0005 folder, never the CUSA00207 folder around it.</summary>
    private static string AddBloodborne(TestPc pc, string? live = null)
    {
        var game = pc.AddGame("bloodborne", savesFolder: live);
        var folder = game.Roots["saves"];
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void Write(string folder, string relative, string content, DateTime? modifiedUtc = null)
    {
        var path = Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, modifiedUtc ?? DateTime.UtcNow);
    }

    private static string Read(string folder, string relative) => File.ReadAllText(Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar)));

    private static BlobId Hash(string content) => BlobId.FromHash(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private static void Zip(string path, params (string Name, string Content)[] entries)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open());
            writer.Write(content);
        }
    }

    private static SortedDictionary<string, string> Tree(string folder) =>
        new(Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(
            f => Path.GetRelativePath(folder, f),
            f => $"{Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(f)))} {File.GetLastWriteTimeUtc(f):O}"),
            StringComparer.OrdinalIgnoreCase);
}
