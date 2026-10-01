namespace GameSync.Core.Art;

/// <summary>
/// Art a game carries in its own folder (KAN-59), for a game Steam has none for, such as a console game kept on the PC:
/// a PS4 dump's <c>sce_sys</c> folder has <c>icon0.png</c> (its 512 × 512 icon, with the logo) and <c>pic1.png</c> (its
/// 1920 × 1080 background); a PS3 game's <c>PS3_GAME</c> folder has <c>ICON0.PNG</c> and <c>PIC1.PNG</c>. Nothing is
/// asked of any website. The files are untrusted like downloaded art: <see cref="ArtCache.CopyLocal"/> checks them.
/// </summary>
public static class LocalArt
{
    /// <summary>How many folders down from the game's folder to look: <c>GameFiles\CUSA03173\sce_sys</c> is three.</summary>
    private const int Depth = 3;

    private static readonly EnumerationOptions Folders = new()
    {
        RecurseSubdirectories = true,
        MaxRecursionDepth = Depth - 1,
        IgnoreInaccessible = true,
        MatchCasing = MatchCasing.CaseInsensitive,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
    };

    /// <summary>The icon (for the cover) and the background (for the hero) in the game's folder, or nulls.</summary>
    public static (string? Cover, string? Hero) Find(string? installDir)
    {
        if (installDir is null || !Directory.Exists(installDir))
        {
            return (null, null);
        }

        try
        {
            // The base game's folder before a patch's or an update's, which carry the same pictures or older ones.
            var candidates = new[] { installDir }
                .Concat(Directory.EnumerateDirectories(installDir, "*", Folders))
                .Where(d => Path.GetFileName(d) is { } name &&
                    (name.Equals("sce_sys", StringComparison.OrdinalIgnoreCase) || name.Equals("PS3_GAME", StringComparison.OrdinalIgnoreCase)))
                .OrderBy(d => d.Contains("patch", StringComparison.OrdinalIgnoreCase) || d.Contains("update", StringComparison.OrdinalIgnoreCase))
                .ThenBy(d => d.Length)
                .ToList();
            foreach (var folder in candidates)
            {
                var cover = First(folder, "icon0.png");
                var hero = First(folder, "pic1.png") ?? First(folder, "pic0.png");
                if (cover is not null || hero is not null)
                {
                    return (cover, hero);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        return (null, null);
    }

    private static string? First(string folder, string name) =>
        Directory.EnumerateFiles(folder, name, new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive, IgnoreInaccessible = true }).FirstOrDefault();

    /// <summary>The smallest program icon worth a cover: below this a title cover reads better.</summary>
    public const int IconAtLeast = 128;

    /// <summary>
    /// KAN-70: a program's own icon as PNG bytes, for a game nothing else has art for (Friday Night Funkin', MAME): the
    /// largest in its first icon group, if it's <see cref="IconAtLeast"/> or more. The program file's resources are read
    /// as data; nothing is loaded or run. An icon kept as PNG comes as it is; a 32-bit bitmap is made into a PNG. Null
    /// when there's none, it's smaller, or the file isn't a program GameSync can read.
    /// </summary>
    public static byte[]? ProgramIcon(string? program)
    {
        if (program is null || !File.Exists(program))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(program);
            using var pe = new System.Reflection.PortableExecutable.PEReader(stream);
            if (pe.PEHeaders.PEHeader?.ResourceTableDirectory is not { RelativeVirtualAddress: > 0, Size: > 0 } table)
            {
                return null;
            }

            var resources = pe.GetSectionData(table.RelativeVirtualAddress).GetContent();
            var group = FirstData(pe, resources, 14);
            if (group is not { Length: >= 6 })
            {
                return null;
            }

            var count = BitConverter.ToUInt16(group, 4);
            if (group.Length < 6 + count * 14)
            {
                return null;
            }

            var best = Enumerable.Range(0, count)
                .Select(i => 6 + i * 14)
                .Select(o => (Size: group[o] == 0 ? 256 : group[o], Bits: BitConverter.ToUInt16(group, o + 6), Id: BitConverter.ToUInt16(group, o + 12)))
                .OrderByDescending(e => e.Size)
                .ThenByDescending(e => e.Bits)
                .FirstOrDefault();
            if (best.Size < IconAtLeast || Data(pe, resources, 3, best.Id) is not { Length: > 8 } image)
            {
                return null;
            }

            return image.AsSpan(0, 8).SequenceEqual(Png.Signature) ? image : Png.FromIconBitmap(image);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    // The resource tree: a type, then a name or number, then a language, each a directory of 8-byte entries after a
    // 16-byte header; a leaf gives the data's RVA and size.
    private static byte[]? FirstData(System.Reflection.PortableExecutable.PEReader pe, System.Collections.Immutable.ImmutableArray<byte> resources, uint type) =>
        Entry(resources, 0, type) is { } names && FirstEntry(resources, names) is { } languages && FirstEntry(resources, languages) is { } leaf
            ? Leaf(pe, resources, leaf)
            : null;

    private static byte[]? Data(System.Reflection.PortableExecutable.PEReader pe, System.Collections.Immutable.ImmutableArray<byte> resources, uint type, uint id) =>
        Entry(resources, 0, type) is { } names && Entry(resources, names, id) is { } languages && FirstEntry(resources, languages) is { } leaf
            ? Leaf(pe, resources, leaf)
            : null;

    // The entry with this number in the directory at `offset`: its target's offset, or null.
    private static int? Entry(System.Collections.Immutable.ImmutableArray<byte> r, int offset, uint id)
    {
        var (named, numbered) = (U16(r, offset + 12), U16(r, offset + 14));
        for (var i = named; i < named + numbered; i++)
        {
            var entry = offset + 16 + i * 8;
            if (U32(r, entry) == id)
            {
                return (int)(U32(r, entry + 4) & 0x7FFFFFFF);
            }
        }

        return null;
    }

    private static int? FirstEntry(System.Collections.Immutable.ImmutableArray<byte> r, int offset) =>
        U16(r, offset + 12) + U16(r, offset + 14) > 0 ? (int)(U32(r, offset + 16 + 4) & 0x7FFFFFFF) : null;

    private static byte[]? Leaf(System.Reflection.PortableExecutable.PEReader pe, System.Collections.Immutable.ImmutableArray<byte> r, int offset)
    {
        var (rva, size) = ((int)U32(r, offset), (int)U32(r, offset + 4));
        if (size <= 0 || size > 4_000_000)
        {
            return null;
        }

        var block = pe.GetSectionData(rva);
        return block.Length >= size ? block.GetReader().ReadBytes(size) : null;
    }

    private static ushort U16(System.Collections.Immutable.ImmutableArray<byte> r, int at) =>
        at + 2 <= r.Length ? BitConverter.ToUInt16(r.AsSpan(at, 2)) : throw new BadImageFormatException("A resource entry runs past its section.");

    private static uint U32(System.Collections.Immutable.ImmutableArray<byte> r, int at) =>
        at + 4 <= r.Length ? BitConverter.ToUInt32(r.AsSpan(at, 4)) : throw new BadImageFormatException("A resource entry runs past its section.");
}

/// <summary>Just enough PNG to keep an icon: RGBA, 8 bits a channel, no filter, in one zlib stream (KAN-70).</summary>
internal static class Png
{
    public static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>A 32-bit icon bitmap (a BITMAPINFOHEADER, then the colours bottom-up, then the AND mask) as a PNG; null for any other kind.</summary>
    public static byte[]? FromIconBitmap(byte[] dib)
    {
        if (dib.Length < 40 || BitConverter.ToInt32(dib, 0) < 40 || BitConverter.ToUInt16(dib, 14) != 32)
        {
            return null;
        }

        var header = BitConverter.ToInt32(dib, 0);
        var width = BitConverter.ToInt32(dib, 4);
        var height = BitConverter.ToInt32(dib, 8) / 2;
        if (width is <= 0 or > 1024 || height is <= 0 or > 1024 || header + width * height * 4 > dib.Length)
        {
            return null;
        }

        // Old icons keep their shape only in the AND mask, with every alpha 0.
        var noAlpha = true;
        for (var i = 0; i < width * height && noAlpha; i++)
        {
            noAlpha = dib[header + i * 4 + 3] == 0;
        }

        var maskStride = (width + 31) / 32 * 4;
        var maskStart = header + width * height * 4;
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            var source = header + (height - 1 - y) * width * 4;
            for (var x = 0; x < width; x++)
            {
                var (s, o) = (source + x * 4, (y * width + x) * 4);
                var alpha = dib[s + 3];
                if (noAlpha)
                {
                    var maskByte = maskStart + (height - 1 - y) * maskStride + x / 8;
                    alpha = maskByte < dib.Length && (dib[maskByte] & (0x80 >> (x % 8))) != 0 ? (byte)0 : (byte)255;
                }

                (rgba[o], rgba[o + 1], rgba[o + 2], rgba[o + 3]) = (dib[s + 2], dib[s + 1], dib[s], alpha);
            }
        }

        return Encode(rgba, width, height);
    }

    public static byte[] Encode(byte[] rgba, int width, int height)
    {
        using var raw = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(raw, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            for (var y = 0; y < height; y++)
            {
                zlib.WriteByte(0);
                zlib.Write(rgba, y * width * 4, width * 4);
            }
        }

        using var png = new MemoryStream();
        png.Write(Signature);
        var ihdr = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        (ihdr[8], ihdr[9]) = (8, 6);
        Chunk(png, "IHDR", ihdr);
        Chunk(png, "IDAT", raw.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream png, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        png.Write(number);
        var typed = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        png.Write(typed);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(number, Crc(typed));
        png.Write(number);
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++)
        {
            c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        }

        return c;
    }).ToArray();

    private static uint Crc(byte[] bytes)
    {
        var c = 0xFFFFFFFF;
        foreach (var b in bytes)
        {
            c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        }

        return c ^ 0xFFFFFFFF;
    }
}
