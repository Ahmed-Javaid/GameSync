using System.Buffers.Binary;
using System.Collections.Frozen;

namespace GameSync.Core.Safety;

/// <summary>R1: saves are data, so anything Windows could run is never backed up, restored or shared.</summary>
public static class ProgramFileDetector
{
    public const int HeaderBytes = 4096;

    public static readonly FrozenSet<string> BlockedExtensions = new[]
    {
        ".exe", ".dll", ".sys", ".scr", ".com", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse",
        ".wsf", ".wsh", ".hta", ".msi", ".msp", ".lnk", ".url", ".reg", ".cpl", ".jar", ".pif", ".scf", ".ocx",
        ".drv", ".appx", ".msix", ".application", ".gadget", ".inf",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static bool HasBlockedExtension(string path) => BlockedExtensions.Contains(Path.GetExtension(path));

    /// <summary>True when the bytes start like a Windows program: "MZ", and "PE\0\0" where the DOS header points.</summary>
    public static bool LooksLikeProgram(ReadOnlySpan<byte> head)
    {
        if (head.Length < 64 || head[0] != (byte)'M' || head[1] != (byte)'Z')
        {
            return false;
        }

        var peOffset = BinaryPrimitives.ReadInt32LittleEndian(head[0x3C..]);
        return peOffset >= 64 && peOffset <= head.Length - 4 && head.Slice(peOffset, 4).SequenceEqual("PE\0\0"u8);
    }

    public static bool IsProgramFile(string fullPath)
    {
        if (HasBlockedExtension(fullPath))
        {
            return true;
        }

        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        Span<byte> head = stackalloc byte[HeaderBytes];
        var read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        return LooksLikeProgram(head[..read]);
    }
}
