using System.Diagnostics;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.Safety;
using GameSync.Core.Scanning;

namespace GameSync.Core.Tests;

public class SafetyTests
{
    [Theory]
    [InlineData("save.exe")]
    [InlineData("SAVE.DLL")]
    [InlineData("shortcut.lnk")]
    [InlineData("script.ps1")]
    [InlineData("run.bat")]
    [InlineData("tweak.reg")]
    public void R1_program_extensions_are_blocked(string name) => Assert.True(ProgramFileDetector.HasBlockedExtension(name));

    [Theory]
    [InlineData("slot1.sav")]
    [InlineData("S0000.sl2")]
    [InlineData("profile.json")]
    [InlineData("world.wld")]
    public void R1_save_extensions_are_allowed(string name) => Assert.False(ProgramFileDetector.HasBlockedExtension(name));

    [Fact]
    public void R1_program_renamed_to_a_save_is_caught_by_its_header() => Assert.True(ProgramFileDetector.LooksLikeProgram(FakeProgram()));

    [Fact]
    public void R1_data_starting_with_MZ_but_no_program_header_is_a_save()
    {
        var bytes = new byte[512];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';

        Assert.False(ProgramFileDetector.LooksLikeProgram(bytes));
    }

    [Fact]
    public void R5_sensitive_folders_cant_be_save_roots()
    {
        var guard = new SensitivePathGuard([(@"C:\Users\You\.ssh", "keys"), (@"C:\Users\You\AppData\Local\Google\Chrome\User Data", "browser")]);

        Assert.NotNull(guard.CheckRoot(@"C:\Users\You\.ssh"));
        Assert.NotNull(guard.CheckRoot(@"C:\Users\You\.ssh\keys"));
        Assert.NotNull(guard.CheckRoot(@"C:\Users\You"));
        Assert.NotNull(guard.CheckRoot(@"C:\Users\You\AppData\Local"));
        Assert.NotNull(guard.CheckRoot(@"C:\"));
        Assert.Null(guard.CheckRoot(@"C:\Users\You\Documents\My Games\Terraria"));
    }

    [Fact]
    public void R5_adding_the_ssh_folder_as_a_save_path_is_refused()
    {
        var ssh = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
        var game = new GameDefinition
        {
            Id = GameId.Parse("sneaky"),
            Title = "Sneaky",
            Roots = new Dictionary<string, string> { ["saves"] = ssh },
            Rules = [new SaveRule { Root = "saves" }],
        };

        var problems = GameValidator.Problems(game, SensitivePathGuard.ForThisPc(Path.Combine(Path.GetTempPath(), "gamesync-data")));

        Assert.Contains(problems, p => p.Contains("passwords or keys", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("saves/../evil.txt")]
    [InlineData("saves/a/../../evil.txt")]
    [InlineData("saves/C:/Windows/evil.dll")]
    [InlineData("saves/save.sav:hidden")]
    [InlineData("saves/CON")]
    [InlineData("saves/nul.txt")]
    [InlineData("saves/a/./b.sav")]
    [InlineData("saves/a\\b.sav")]
    [InlineData("saves/name.")]
    [InlineData("saves/name ")]
    [InlineData("saves//b.sav")]
    [InlineData("../saves/b.sav")]
    [InlineData("/saves/b.sav")]
    [InlineData("saves")]
    public void R6_unsafe_paths_from_a_version_are_refused(string path) =>
        Assert.Throws<UnsafePathException>(() => RestorePathGuard.Split(path));

    [Fact]
    public void R6_a_link_inside_the_save_folder_is_refused()
    {
        var root = Path.Combine(Path.GetTempPath(), "gamesync-tests", Guid.NewGuid().ToString("N")[..12]);
        var saves = Directory.CreateDirectory(Path.Combine(root, "saves")).FullName;
        var outside = Directory.CreateDirectory(Path.Combine(root, "outside")).FullName;
        try
        {
            using var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{Path.Combine(saves, "link")}\" \"{outside}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            })!;
            mklink.WaitForExit();
            Assert.Equal(0, mklink.ExitCode);

            Assert.Throws<UnsafePathException>(() => RestorePathGuard.Resolve(saves, "link/evil.sav"));
            Assert.EndsWith(@"saves\fine.sav", RestorePathGuard.Resolve(saves, "fine.sav"));
        }
        finally
        {
            Directory.Delete(Path.Combine(saves, "link"));
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void R7_a_version_that_writes_a_Run_value_is_refused()
    {
        string[] approved = [@"HKCU\Software\Studio MDHR\Cuphead"];

        Assert.Null(RegistryGuard.Check(@"HKCU\Software\Studio MDHR\Cuphead", approved));
        Assert.Null(RegistryGuard.Check(@"HKEY_CURRENT_USER\Software\Studio MDHR\Cuphead\Slots", approved));
        Assert.NotNull(RegistryGuard.Check(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", approved));
        Assert.NotNull(RegistryGuard.Check(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", [@"HKCU\Software\Microsoft\Windows\CurrentVersion\Run"]));
        Assert.NotNull(RegistryGuard.Check(@"HKLM\Software\Studio MDHR\Cuphead", approved));
        Assert.NotNull(RegistryGuard.Check(@"HKCU\Software\Other Game", approved));
        Assert.NotNull(RegistryGuard.Check(@"HKCU\Software\Studio MDHR\Cuphead\..\..\Microsoft", approved));
    }

    [Fact]
    public void FIND_11_logs_crash_dumps_and_caches_are_excluded()
    {
        using var world = new TestWorld();
        using var pc = world.Pc("DESKTOP");
        var game = pc.AddGame("game");
        pc.Write("game", "slot1.sav", "progress");
        pc.Write("game", "logs/output.txt", "log");
        pc.Write("game", "Saved/Logs/game.log", "log");
        pc.Write("game", "crash.dmp", "dump");
        pc.Write("game", "ShaderCache/a.bin", "shader");
        pc.Write("game", "web/Cache/data_1", "cache");

        var snapshot = new SnapshotScanner(SensitivePathGuard.ForThisPc(pc.DataDir)).Scan(game, _ => true);

        Assert.Equal("saves/slot1.sav", Assert.Single(snapshot.Files).Path);
    }

    [Fact]
    public void Glob_matches_folders_and_files_as_documented()
    {
        Assert.True(new Glob("**").IsMatch("a/b/c.sav"));
        Assert.True(new Glob("*.sav").IsMatch("slot1.SAV"));
        Assert.False(new Glob("*.sav").IsMatch("sub/slot1.sav"));
        Assert.True(new Glob("**/*.sav").IsMatch("slot1.sav"));
        Assert.True(new Glob("**/*.sav").IsMatch("a/b/slot1.sav"));
        Assert.True(new Glob("**/logs/**").IsMatch("logs/x.txt"));
        Assert.True(new Glob("Saves/**").IsMatch("saves/a/b"));
        Assert.False(new Glob("Saves/**").IsMatch("other/a"));
    }

    internal static byte[] FakeProgram()
    {
        var bytes = new byte[512];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        BitConverter.GetBytes(0x80).CopyTo(bytes, 0x3C);
        "PE\0\0"u8.CopyTo(bytes.AsSpan(0x80));
        return bytes;
    }
}
