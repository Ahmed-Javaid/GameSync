using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameSync.Host;

// R19, PKG-03: GameSync's release key and the signed manifest beside each installer (design.md → Packaging).
//   dotnet run --project tools/GameSync.Release -- keygen
//   dotnet run --project tools/GameSync.Release -- sign <installer> --version 1.0.1 [--key <pem>]
//   dotnet run --project tools/GameSync.Release -- verify <installer> [<sig>]
//   dotnet run --project tools/GameSync.Release -- feed <folder> --version 1.0.1 --base <url> [--notes <file>]
var list = args.ToList();
var root = Root();
try
{
    switch (list.Count == 0 ? "" : list[0])
    {
        case "keygen":
            return KeyGen(root);
        case "sign":
            return Sign(list.Skip(1).ToList(), root);
        case "verify":
            return Verify(list.Skip(1).ToList());
        case "feed":
            return Feed(list.Skip(1).ToList());
        default:
            Console.Error.WriteLine("""
                GameSync's release key, and the signature beside each installer (R19, PKG-03).

                keygen                       makes the key: its private half into notes\release-key.pem (never the
                                             repository: keep a copy somewhere safe, such as a password manager), its
                                             public half into src\GameSync.Host\ReleaseKey.cs. Won't replace a key.
                sign <installer> --version <1.0.1> [--key <pem>]
                                             writes GameSync-Setup-<version>.sig beside the installer, which must be
                                             named GameSync-Setup-<version>.exe
                verify <installer> [<sig>]   checks a release as the updater does, with the key built into GameSync
                feed <folder> --version <1.0.1> --base <url> [--notes <file>]
                                             writes latest.json there, shaped like GitHub's latest release, for trying
                                             the updater without GitHub: set GAMESYNC_UPDATE_FEED to its address
                """);
            return 2;
    }
}
catch (Exception e) when (e is ReleaseRefusedException or IOException or ArgumentException or CryptographicException or FormatException)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}

static int KeyGen(string root)
{
    var pem = Path.Combine(root, "notes", "release-key.pem");
    if (File.Exists(pem))
    {
        Console.Error.WriteLine($"There's a release key already: {pem}. A new one would refuse every update to a GameSync built with the old one.");
        return 1;
    }

    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    Directory.CreateDirectory(Path.GetDirectoryName(pem)!);
    File.WriteAllText(pem, key.ExportPkcs8PrivateKeyPem() + "\n");
    var publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
    var source = Path.Combine(root, "src", "GameSync.Host", "ReleaseKey.cs");
    File.WriteAllText(source, $$"""
        namespace GameSync.Host;

        /// <summary>
        /// R19: the public half of GameSync's release key, which every update's signature must check out with. Made by
        /// <c>tools/GameSync.Release keygen</c> on {{DateTime.Now.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}}, which writes this file; its private half stays with the owner,
        /// out of the repository.
        /// </summary>
        public static class ReleaseKey
        {
            public const string Public = "{{publicKey}}";
        }

        """.ReplaceLineEndings("\r\n"));
    Console.WriteLine($"The release key's private half is in {pem}. Keep a copy somewhere safe (a password manager): without it, a new release can't be signed and every GameSync out there refuses it.");
    Console.WriteLine($"Its public half is built into GameSync from {source}.");
    return 0;
}

static int Sign(List<string> rest, string root)
{
    var version = Version.Parse(Take(rest, "--version") ?? throw new ArgumentException("Say which version: --version 1.0.1."));
    var pem = Take(rest, "--key") ?? Path.Combine(root, "notes", "release-key.pem");
    var installer = rest.FirstOrDefault() ?? throw new ArgumentException("Say which installer to sign.");
    if (!string.Equals(Path.GetFileName(installer), ReleaseManifest.NameFor(version), StringComparison.Ordinal))
    {
        throw new ArgumentException($"A release's installer is named {ReleaseManifest.NameFor(version)}.");
    }

    if (!File.Exists(pem))
    {
        throw new ArgumentException($"No release key at {pem}. Make one with: dotnet run --project tools/GameSync.Release -- keygen");
    }

    using var key = ECDsa.Create();
    key.ImportFromPem(File.ReadAllText(pem));
    if (Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()) != ReleaseKey.Public)
    {
        throw new ArgumentException($"{pem} isn't the key whose public half is built into GameSync (src\\GameSync.Host\\ReleaseKey.cs).");
    }

    var signed = ReleaseManifest.Of(installer, version).Sign(key);
    var sig = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(installer))!, ReleaseManifest.SignatureNameFor(version));
    File.WriteAllText(sig, signed, new UTF8Encoding(false));
    ReleaseManifest.Verify(signed, ReleaseKey.Public);
    Console.WriteLine($"Signed: {sig}");
    return 0;
}

static int Verify(List<string> rest)
{
    var installer = rest.FirstOrDefault() ?? throw new ArgumentException("Say which installer to check.");
    var sig = rest.Skip(1).FirstOrDefault() ?? Path.ChangeExtension(installer, ".sig");
    var manifest = ReleaseManifest.Verify(File.ReadAllText(sig), ReleaseKey.Public);
    using var file = File.OpenRead(installer);
    if (Path.GetFileName(installer) != manifest.File || !manifest.Describes(file))
    {
        throw new ReleaseRefusedException($"{installer} isn't the installer its signature describes.");
    }

    Console.WriteLine($"GameSync {manifest.Version.ToString(3)}: signed with GameSync's release key, and the installer is the one it describes.");
    return 0;
}

static int Feed(List<string> rest)
{
    var version = Version.Parse(Take(rest, "--version") ?? throw new ArgumentException("Say which version: --version 1.0.1."));
    var baseUrl = (Take(rest, "--base") ?? throw new ArgumentException("Say where the files are served: --base http://127.0.0.1:5179/")).TrimEnd('/') + "/";
    var notes = Take(rest, "--notes") is { } file ? File.ReadAllText(file) : "- What's new goes here.";
    var folder = rest.FirstOrDefault() ?? throw new ArgumentException("Say which folder holds the installer and its signature.");
    var installer = Path.Combine(folder, ReleaseManifest.NameFor(version));
    var sig = Path.Combine(folder, ReleaseManifest.SignatureNameFor(version));
    var feed = new
    {
        tag_name = $"v{version.ToString(3)}",
        name = $"GameSync {version.ToString(3)}",
        html_url = baseUrl + "release.html",
        body = notes,
        draft = false,
        prerelease = false,
        assets = new[]
        {
            new { name = Path.GetFileName(installer), size = new FileInfo(installer).Length, browser_download_url = baseUrl + Path.GetFileName(installer) },
            new { name = Path.GetFileName(sig), size = new FileInfo(sig).Length, browser_download_url = baseUrl + Path.GetFileName(sig) },
        },
    };
    var path = Path.Combine(folder, "latest.json");
    File.WriteAllText(path, JsonSerializer.Serialize(feed, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Wrote {path}: set GAMESYNC_UPDATE_FEED={baseUrl}latest.json to try it.");
    return 0;
}

static string? Take(List<string> list, string name)
{
    var at = list.IndexOf(name);
    if (at < 0 || at + 1 >= list.Count)
    {
        return null;
    }

    var value = list[at + 1];
    list.RemoveRange(at, 2);
    return value;
}

static string Root()
{
    for (var folder = new DirectoryInfo(Environment.CurrentDirectory); folder is not null; folder = folder.Parent)
    {
        if (File.Exists(Path.Combine(folder.FullName, "GameSync.sln")))
        {
            return folder.FullName;
        }
    }

    throw new IOException("Run this inside GameSync's repository.");
}
