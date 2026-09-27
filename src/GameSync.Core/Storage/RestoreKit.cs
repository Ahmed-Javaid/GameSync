using System.Security.Cryptography;

namespace GameSync.Core.Storage;

/// <summary>
/// <c>HOW-TO-RESTORE.txt</c> and <c>restore.ps1</c>, written at the top of every cloud folder so saves come back
/// without GameSync (CLOUD-03).
/// </summary>
public static class RestoreKit
{
    private static readonly string[] Names = ["HOW-TO-RESTORE.txt", "restore.ps1"];

    public static IReadOnlyDictionary<string, byte[]> Files { get; } = Names.ToDictionary(name => name, Read);

    /// <summary>Changes whenever either file does, so a backend rewrites them only then.</summary>
    public static string Version { get; } =
        Convert.ToHexStringLower(SHA256.HashData(Files.OrderBy(f => f.Key, StringComparer.Ordinal).SelectMany(f => f.Value).ToArray()))[..16];

    private static byte[] Read(string name)
    {
        using var stream = typeof(RestoreKit).Assembly.GetManifestResourceStream($"GameSync.RestoreKit.{name}")
            ?? throw new InvalidOperationException($"The build is missing {name}.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
