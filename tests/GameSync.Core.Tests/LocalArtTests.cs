using GameSync.Core.Art;
using GameSync.Core.Discovery;
using GameSync.Core.Model;
using GameSync.Host;
using GameSync.UI.Theming;
using GameSync.UI.ViewModels;

namespace GameSync.Core.Tests;

/// <summary>
/// KAN-59 art from a game's own folder (a PS4 dump's sce_sys pictures) and the cover made from a square picture; KAN-55
/// the store's mark on a game's page; KAN-56 Properties in a game's right-click menu.
/// </summary>
public class LocalArtTests
{
    // A PNG's signature and header: what the content check looks at.
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 1, 0, 0, 0, 1, 8, 6, 0, 0, 0];

    [Fact]
    public void KAN_59_a_PS4_dump_s_own_icon_and_background_become_its_cover_and_banner_the_base_game_s_before_a_patch_s()
    {
        using var world = new TestWorld();
        var game = Path.Combine(world.Root, "G", "Bloodborne GOTY");
        Write(Path.Combine(game, "GameFiles", "CUSA03173-patch", "sce_sys", "icon0.png"), [.. Png, 2]);
        Write(Path.Combine(game, "GameFiles", "CUSA03173", "sce_sys", "icon0.png"), [.. Png, 1]);
        Write(Path.Combine(game, "GameFiles", "CUSA03173", "sce_sys", "pic1.png"), [.. Png, 1, 1]);
        Write(Path.Combine(game, "BB_Launcher.exe"), "MZ"u8.ToArray());

        var (cover, hero) = LocalArt.Find(game);
        Assert.Equal(Path.Combine(game, "GameFiles", "CUSA03173", "sce_sys", "icon0.png"), cover);
        Assert.Equal(Path.Combine(game, "GameFiles", "CUSA03173", "sce_sys", "pic1.png"), hero);
        Assert.Equal((null, null), LocalArt.Find(Path.Combine(world.Root, "nowhere")));
        Assert.Equal((null, null), LocalArt.Find(null));

        // Copied into the art cache, checked by content; a second copy of the same is left alone.
        var data = Path.Combine(world.Root, "data");
        using var art = new ArtCache(data);
        var id = GameId.Parse("bloodborne-goty");
        Assert.Equal(2, art.CopyLocal(id, game));
        Assert.Equal(0, art.CopyLocal(id, game));
        Assert.Equal(Path.Combine(art.LocalFolder, "bloodborne-goty", "cover.png"), art.FindLocal(id, ArtKind.Cover));
        Assert.NotNull(art.FindLocal(id, ArtKind.Hero));
        Assert.Null(art.FindLocal(id, ArtKind.Logo));

        // Something only named like a picture is refused (ART-08).
        var fake = Path.Combine(world.Root, "G", "Fake");
        Write(Path.Combine(fake, "sce_sys", "icon0.png"), "MZ this is a program"u8.ToArray());
        Assert.Equal(0, art.CopyLocal(GameId.Parse("fake"), fake));
        Assert.Null(art.FindLocal(GameId.Parse("fake"), ArtKind.Cover));
    }

    [Fact]
    public void KAN_59_a_square_picture_makes_a_2_by_3_cover_with_the_picture_whole_in_the_middle_over_a_dimmed_copy()
    {
        // A 4 x 4 picture: rows of four colours.
        var picture = new byte[4 * 4 * 4];
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                var i = (y * 4 + x) * 4;
                (picture[i], picture[i + 1], picture[i + 2], picture[i + 3]) = ((byte)(60 * y), 200, (byte)(50 * x), 255);
            }
        }

        Assert.False(CoverArt.IsCover(4, 4));
        Assert.True(CoverArt.IsCover(600, 900));
        Assert.Equal(6, CoverArt.HeightFor(4));
        var cover = CoverArt.Compose(picture, 4, 4);
        Assert.Equal(4 * 6 * 4, cover.Length);

        // Rows 1 to 4 are the picture itself; the rows above and below are its blurred copy, dimmed.
        Assert.Equal(picture, cover[(4 * 4)..(5 * 4 * 4)]);
        foreach (var edge in new[] { 0, 5 })
        {
            for (var x = 0; x < 4; x++)
            {
                var i = (edge * 4 + x) * 4;
                Assert.True(cover[i + 1] <= 200 * 0.46, $"row {edge} is dimmed");
                Assert.Equal(255, cover[i + 3]);
            }
        }
    }

    [Fact]
    public void KAN_55_a_game_s_page_shows_where_it_s_installed_as_a_mark_with_its_words_under_the_pointer()
    {
        LauncherGame Game(StoreKind? store, bool installed) => new() { Id = GameId.Parse("game"), Title = "Game", Store = store, Installed = installed };

        Assert.Equal(("", "steam", "Installed through Steam"), GameViewModel.EyebrowOf(Game(StoreKind.Steam, true), null));
        Assert.Equal(("", "epic", "Installed through the Epic Games Launcher"), GameViewModel.EyebrowOf(Game(StoreKind.Epic, true), null));
        Assert.Equal(("", "ea", "Installed through the EA app"), GameViewModel.EyebrowOf(Game(StoreKind.Ea, true), null));
        Assert.Equal(("Not installed on this PC", "steam", "On Steam; not installed on this PC"), GameViewModel.EyebrowOf(Game(StoreKind.Steam, false), null));
        Assert.Equal("folder", GameViewModel.EyebrowOf(Game(StoreKind.Loose, true), null).Icon);
        Assert.Equal(("Not installed on this PC · Found by its saves", null, null), GameViewModel.EyebrowOf(Game(null, false), null));

        // The marks are filled shapes the icon control knows; an unknown name isn't one.
        Assert.True(UI.Controls.Icons.IsBrand("steam") && UI.Controls.Icons.IsBrand("epic") && UI.Controls.Icons.IsBrand("ea"));
        Assert.False(UI.Controls.Icons.IsBrand("folder"));
    }

    [Fact]
    public void KAN_56_a_game_s_right_click_menu_opens_its_Properties()
    {
        var opened = new List<(GameId, string?)>();
        var actions = new LauncherActions(_ => { }, () => { }, (_, _) => { }, (_, _) => { }) { OpenProperties = (game, section) => opened.Add((game, section)) };
        var tile = HomeViewModel.Tile(new LauncherGame { Id = GameId.Parse("hades"), Title = "Hades" }, DateTime.Now, actions: actions);

        Assert.True(tile.HasProperties && tile.HasMenu);
        tile.PropertiesCommand!.Execute(null);
        Assert.Equal([(GameId.Parse("hades"), (string?)null)], opened);
        Assert.False(HomeViewModel.Tile(new LauncherGame { Id = GameId.Parse("hades"), Title = "Hades" }, DateTime.Now).HasProperties);
    }

    [Fact]
    public void KAN_70_a_game_with_no_other_art_takes_its_program_s_icon_as_its_cover()
    {
        using var world = new TestWorld();
        var rgba = Enumerable.Range(0, 256 * 256).SelectMany(i => new byte[] { (byte)(i % 256), (byte)(i / 256), 200, 255 }).ToArray();
        var png = Core.Art.Png.Encode(rgba, 256, 256);

        // An icon kept as PNG comes out as it is; a 32-bit bitmap comes out as a PNG of its size; a small icon isn't used.
        var withPng = Path.Combine(world.Root, "Funkin", "Funkin.exe");
        Write(withPng, TinyProgram(png, 256));
        Assert.Equal(png, LocalArt.ProgramIcon(withPng));

        var withBitmap = Path.Combine(world.Root, "BlackOps3", "BlackOps3.exe");
        Write(withBitmap, TinyProgram(IconBitmap(256), 256));
        var converted = LocalArt.ProgramIcon(withBitmap)!;
        Assert.Equal(Core.Art.Png.Signature, converted[..8]);
        Assert.Equal((256, 256), (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(converted.AsSpan(16)), System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(converted.AsSpan(20))));

        var small = Path.Combine(world.Root, "Old", "old.exe");
        Write(small, TinyProgram(IconBitmap(64), 64));
        Assert.Null(LocalArt.ProgramIcon(small));
        Assert.Null(LocalArt.ProgramIcon(Path.Combine(world.Root, "missing.exe")));
        Write(Path.Combine(world.Root, "not-a-program.exe"), new byte[5000]);
        Assert.Null(LocalArt.ProgramIcon(Path.Combine(world.Root, "not-a-program.exe")));

        // The art refresh keeps it as the game's cover when its folder has nothing else, and leaves it be the next time.
        using var art = new ArtCache(Path.Combine(world.Root, "data"));
        var fnf = GameId.Parse("fnf");
        Assert.Equal(1, art.CopyLocal(fnf, Path.GetDirectoryName(withPng), withPng));
        Assert.Equal(png, File.ReadAllBytes(art.FindLocal(fnf, ArtKind.Cover)!));
        Assert.Equal(0, art.CopyLocal(fnf, Path.GetDirectoryName(withPng), withPng));
    }

    // A 32-bit icon bitmap as a program keeps it: a BITMAPINFOHEADER with the height doubled, the colours bottom-up, then the mask.
    private static byte[] IconBitmap(int size)
    {
        var dib = new byte[40 + size * size * 4 + (size + 31) / 32 * 4 * size];
        BitConverter.GetBytes(40).CopyTo(dib, 0);
        BitConverter.GetBytes(size).CopyTo(dib, 4);
        BitConverter.GetBytes(size * 2).CopyTo(dib, 8);
        BitConverter.GetBytes((ushort)1).CopyTo(dib, 12);
        BitConverter.GetBytes((ushort)32).CopyTo(dib, 14);
        for (var i = 0; i < size * size; i++)
        {
            (dib[40 + i * 4], dib[40 + i * 4 + 1], dib[40 + i * 4 + 2], dib[40 + i * 4 + 3]) = (30, 144, 255, 255);
        }

        return dib;
    }

    // The smallest program file that carries an icon: headers, and one .rsrc section with a named icon group (as Funkin.exe
    // has) pointing at one icon of this size.
    private static byte[] TinyProgram(byte[] icon, int size)
    {
        const int rva = 0x1000, raw = 0x200, iconAt = 0x100;
        var groupAt = iconAt + (icon.Length + 7) / 8 * 8;
        var section = new byte[groupAt + 20];
        void U16(int at, int value) => BitConverter.GetBytes((ushort)value).CopyTo(section, at);
        void U32(int at, uint value) => BitConverter.GetBytes(value).CopyTo(section, at);

        // The root: two types by number, icons (3) and icon groups (14).
        U16(0x0E, 2);
        U32(0x10, 3);
        U32(0x14, 0x80000000 | 0x20);
        U32(0x18, 14);
        U32(0x1C, 0x80000000 | 0x50);

        // Icon 1, in English, and its data.
        U16(0x2E, 1);
        U32(0x30, 1);
        U32(0x34, 0x80000000 | 0x38);
        U16(0x46, 1);
        U32(0x48, 0x409);
        U32(0x4C, 0xA0);

        // One group, by name, in English, and its data.
        U16(0x5C, 1);
        U32(0x60, 0x80000000 | 0xC0);
        U32(0x64, 0x80000000 | 0x68);
        U16(0x76, 1);
        U32(0x78, 0x409);
        U32(0x7C, 0xB0);

        U32(0xA0, (uint)(rva + iconAt));
        U32(0xA4, (uint)icon.Length);
        U32(0xB0, (uint)(rva + groupAt));
        U32(0xB4, 20);
        U16(0xC0, 4);
        System.Text.Encoding.Unicode.GetBytes("ICON").CopyTo(section, 0xC2);
        icon.CopyTo(section, iconAt);

        // GRPICONDIR: an icon, one entry of this size, 32 bits, icon 1.
        U16(groupAt + 2, 1);
        U16(groupAt + 4, 1);
        section[groupAt + 6] = (byte)(size % 256);
        section[groupAt + 7] = (byte)(size % 256);
        U16(groupAt + 10, 1);
        U16(groupAt + 12, 32);
        U32(groupAt + 14, (uint)icon.Length);
        U16(groupAt + 18, 1);

        var rawSize = (section.Length + 0x1FF) / 0x200 * 0x200;
        var file = new byte[raw + rawSize];
        void F16(int at, int value) => BitConverter.GetBytes((ushort)value).CopyTo(file, at);
        void F32(int at, uint value) => BitConverter.GetBytes(value).CopyTo(file, at);
        file[0] = (byte)'M';
        file[1] = (byte)'Z';
        F32(0x3C, 0x40);
        file[0x40] = (byte)'P';
        file[0x41] = (byte)'E';
        F16(0x44, 0x14C);
        F16(0x46, 1);
        F16(0x54, 0xE0);
        F16(0x56, 0x0102);

        const int optional = 0x58;
        F16(optional, 0x10B);
        F32(optional + 28, 0x400000);
        F32(optional + 32, 0x1000);
        F32(optional + 36, 0x200);
        F16(optional + 40, 6);
        F16(optional + 48, 6);
        F32(optional + 56, (uint)(rva + (section.Length + 0xFFF) / 0x1000 * 0x1000));
        F32(optional + 60, raw);
        F16(optional + 68, 2);
        F32(optional + 92, 16);
        F32(optional + 112, rva);
        F32(optional + 116, (uint)section.Length);

        const int header = optional + 0xE0;
        System.Text.Encoding.ASCII.GetBytes(".rsrc").CopyTo(file, header);
        F32(header + 8, (uint)section.Length);
        F32(header + 12, rva);
        F32(header + 16, (uint)rawSize);
        F32(header + 20, raw);
        F32(header + 36, 0x40000040);
        section.CopyTo(file, raw);
        return file;
    }

    private static void Write(string path, byte[] content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }
}
