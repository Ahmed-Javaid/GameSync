using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace GameSync.Host;

/// <summary>
/// R19, PKG-03: what a release says about its installer, signed with GameSync's own release key, as there's no Windows
/// code signing (the owner, 7 Oct 2026: "i dont really wanna pay for digital signing"). Beside each installer on GitHub
/// Releases, <c>GameSync-Setup-1.0.1.sig</c> holds five lines (what it is, the version, the installer's name, its size
/// and its SHA-256), then a signature over exactly those lines made with the key's private half, which never goes into
/// the repository. The updater runs an installer only when the signature checks out with the public half built into
/// GameSync (<see cref="ReleaseKey"/>), and the file is exactly the one the lines describe.
/// </summary>
public sealed partial record ReleaseManifest(Version Version, string File, long Size, string Sha256)
{
    public const string FirstLine = "GameSync release 1";

    /// <summary>The most an installer may be: GameSync's is about 60 MB.</summary>
    public const long MostSize = 1L << 30;

    private const string SignatureLine = "signature ";

    /// <summary>An installer's name: <c>GameSync-Setup-1.0.1.exe</c>.</summary>
    public static string NameFor(Version version) => $"GameSync-Setup-{version.ToString(3)}.exe";

    /// <summary>Its signed manifest's name, beside it: <c>GameSync-Setup-1.0.1.sig</c>.</summary>
    public static string SignatureNameFor(Version version) => $"GameSync-Setup-{version.ToString(3)}.sig";

    /// <summary>The lines the signature covers, each ending in a line feed.</summary>
    public string Body =>
        $"{FirstLine}\nversion {Version.ToString(3)}\nfile {File}\nsize {Size.ToString(CultureInfo.InvariantCulture)}\nsha256 {Sha256}\n";

    /// <summary>The manifest of <paramref name="installer"/>, released as <paramref name="version"/>.</summary>
    public static ReleaseManifest Of(string installer, Version version)
    {
        using var file = System.IO.File.OpenRead(installer);
        return new ReleaseManifest(version, NameFor(version), file.Length, Convert.ToHexStringLower(SHA256.HashData(file)));
    }

    /// <summary>The lines and their signature, made with the release key's private half (the release tool; tests).</summary>
    public string Sign(ECDsa key) =>
        Body + SignatureLine + Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes(Body), HashAlgorithmName.SHA256)) + "\n";

    /// <summary>
    /// The manifest in <paramref name="text"/>, once its signature checks out with <paramref name="publicKey"/> (base64
    /// of the key's SubjectPublicKeyInfo); otherwise <see cref="ReleaseRefusedException"/> saying why, in words for the
    /// person.
    /// </summary>
    public static ReleaseManifest Verify(string text, string publicKey)
    {
        if (string.IsNullOrEmpty(publicKey))
        {
            throw new ReleaseRefusedException("this GameSync has no release key built in to check it with");
        }

        var at = text.LastIndexOf("\n" + SignatureLine, StringComparison.Ordinal);
        if (at < 0)
        {
            throw new ReleaseRefusedException("it carries no signature");
        }

        var body = text[..(at + 1)];
        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(text[(at + 1 + SignatureLine.Length)..].Trim());
        }
        catch (FormatException)
        {
            throw new ReleaseRefusedException("its signature can't be read");
        }

        using var key = ECDsa.Create();
        try
        {
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
        }
        catch (Exception e) when (e is FormatException or CryptographicException)
        {
            throw new ReleaseRefusedException("this GameSync's release key can't be read");
        }

        if (!key.VerifyData(Encoding.UTF8.GetBytes(body), signature, HashAlgorithmName.SHA256))
        {
            throw new ReleaseRefusedException("it isn't signed with GameSync's release key");
        }

        return Parse(body);
    }

    /// <summary>Whether <paramref name="file"/> is exactly the installer these lines describe: its size and SHA-256.</summary>
    public bool Describes(Stream file)
    {
        if (file.Length != Size)
        {
            return false;
        }

        file.Position = 0;
        return Convert.ToHexStringLower(SHA256.HashData(file)) == Sha256;
    }

    private static ReleaseManifest Parse(string body)
    {
        var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length != 5 || lines[0] != FirstLine)
        {
            throw new ReleaseRefusedException("its lines aren't a GameSync release's");
        }

        string Value(int index, string name) =>
            lines[index].StartsWith(name + " ", StringComparison.Ordinal) ? lines[index][(name.Length + 1)..]
                : throw new ReleaseRefusedException("its lines aren't a GameSync release's");

        if (!Version.TryParse(Value(1, "version"), out var version) || version.Build < 0 || version.Revision >= 0)
        {
            throw new ReleaseRefusedException("its version can't be read");
        }

        var file = Value(2, "file");
        if (file != NameFor(version))
        {
            throw new ReleaseRefusedException("it names another file than that version's installer");
        }

        if (!long.TryParse(Value(3, "size"), NumberStyles.None, CultureInfo.InvariantCulture, out var size) || size <= 0 || size > MostSize)
        {
            throw new ReleaseRefusedException("its installer's size can't be right");
        }

        var sha = Value(4, "sha256");
        return Sha256Hex().IsMatch(sha) ? new ReleaseManifest(version, file, size, sha) : throw new ReleaseRefusedException("its installer's fingerprint can't be read");
    }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Hex();
}

/// <summary>R19: an update GameSync won't install, and why, in words that finish "It wasn't installed: …".</summary>
public sealed class ReleaseRefusedException(string reason) : Exception(reason);
