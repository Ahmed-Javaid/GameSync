using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.Safety;
using GameSync.Core.State;
using GameSync.Core.Storage;

namespace GameSync.Core.Sync;

/// <summary>
/// SHARE-08: what a shared zip says about itself, in <see cref="FileName"/> at its top: who packed it and when, and for
/// each game its rules and the versions in it, with every file's place, size, time and hash.
/// </summary>
public sealed record ShareManifest
{
    public const string FileName = "gamesync-share.json";
    public const string ReadmeName = "README.txt";
    public const int CurrentFormat = 1;

    public int Format { get; init; } = CurrentFormat;

    /// <summary>The GameSync that packed it.</summary>
    public required string GameSync { get; init; }

    public required DateTime PackedUtc { get; init; }

    /// <summary>The PC it was packed on, by name.</summary>
    public required string PackedOn { get; init; }

    public required IReadOnlyList<SharedGame> Games { get; init; }
}

/// <summary>A game in a shared zip: its folder there, its rules as every PC reads them, and its versions.</summary>
public sealed record SharedGame
{
    public required GameId Id { get; init; }

    public required string Title { get; init; }

    public required string Folder { get; init; }

    public PortableRules? Rules { get; init; }

    public required IReadOnlyList<SharedVersion> Versions { get; init; }
}

/// <summary>A version in a shared zip: its folder under its game's, where it came from, and its files ("root/relative").</summary>
public sealed record SharedVersion
{
    public required VersionId Id { get; init; }

    public required string Folder { get; init; }

    public required DateTime CreatedUtc { get; init; }

    /// <summary>When the save itself was made: its newest file.</summary>
    public required DateTime SavedUtc { get; init; }

    public required string Pc { get; init; }

    public string? Label { get; init; }

    /// <summary>The account IDs its save folders used (PC-03), for SHARE-12's warning.</summary>
    public IReadOnlyDictionary<string, string>? Accounts { get; init; }

    public required IReadOnlyList<FileEntry> Files { get; init; }
}

/// <summary>What to share of one game: the versions picked, its current one when it's shared whole (SHARE-01).</summary>
public sealed record SharePick(GameId Game, IReadOnlyList<VersionId> Versions);

/// <summary>A finished zip: where it is, what's in it, its size, and anything left out, such as a program file (SHARE-06).</summary>
public sealed record SharePack(string ZipPath, int Games, int Versions, long Bytes, IReadOnlyList<string> Notes);

/// <summary>What importing one game's shared saves came to: versions added, and why any weren't (SHARE-10, SHARE-11).</summary>
public sealed record SharedImport(int Added, int Removed, IReadOnlyList<string> Notes);

/// <summary>
/// Sharing saves (SHARE-01 to SHARE-13): picked versions packed into one zip with a manifest and a README for restoring
/// by hand, and a shared zip's saves added to a game's history as pinned versions, never as current ones.
/// </summary>
public sealed partial class SyncService
{
    /// <summary>A shared zip unpacks to at most this much, so a zip made to fill a disk is refused.</summary>
    public const long MaxShareBytes = 8L * 1024 * 1024 * 1024;

    /// <summary>
    /// SHARE-01 to SHARE-09: packs the versions picked into a zip at <paramref name="zipPath"/>, which must not exist yet.
    /// A version only the cloud keeps comes down first, only its own files (SHARE-05). Every file is checked like a
    /// backup's (R1): a program is left out and said so. The zip is written beside its place and only named once whole, so
    /// a cancelled or failed pack leaves nothing behind (SHARE-09).
    /// </summary>
    /// <param name="describeRoot">A save folder as a person reads it, for the README: "Documents\My Games\Terraria".</param>
    /// <param name="progress">Bytes packed so far, of all the saves' bytes.</param>
    public async Task<SharePack> PackAsync(IReadOnlyList<SharePick> picks, string zipPath, string appVersion, Func<string, string> describeRoot,
        IProgress<(long Done, long Total)>? progress, CancellationToken ct)
    {
        if (File.Exists(zipPath))
        {
            throw new InvalidOperationException($"{zipPath} is there already; GameSync never writes over a file.");
        }

        var plan = new List<(SyncStream Stream, IReadOnlyList<VersionRecord> Versions)>();
        foreach (var pick in picks.Where(p => p.Versions.Count > 0))
        {
            var stream = Main(pick.Game);
            await TryPullAsync(stream, ct);
            var (versions, _) = await LoadVersionsAsync(stream, ct);
            var thinned = await _log.ListThinnedAsync(stream.Id, ct);
            var byId = versions.Where(v => !thinned.Contains(v.Id)).ToDictionary(v => v.Id);
            plan.Add((stream, pick.Versions.Distinct().Select(id => byId.TryGetValue(id, out var version)
                ? version
                : throw new InvalidOperationException($"{stream.Definition.Title} has no version {id} any more.")).OrderBy(SavedAt).ToList()));
        }

        if (plan.Count == 0)
        {
            throw new InvalidOperationException("Pick at least one save to share.");
        }

        var total = plan.Sum(p => p.Versions.Sum(v => FileSet.TotalSize(v.Files)));
        long done = 0;
        progress?.Report((0, total));
        var names = DeviceNames();
        var notes = new List<string>();
        var games = new List<SharedGame>();
        var part = zipPath + ".part";
        var staging = Path.Combine(Path.GetTempPath(), "GameSync", $"share-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            await using (var output = new FileStream(part, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create))
            {
                var gameFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (stream, versions) in plan)
                {
                    var title = stream.Definition.Title;
                    var gameFolder = Unique(gameFolders, FolderName(title, stream.Id.Value));
                    var versionFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var shared = new List<SharedVersion>();
                    foreach (var version in versions)
                    {
                        var pc = names.GetValueOrDefault(version.Device.Id, version.Device.Name);
                        var saved = SavedAt(version);
                        var folder = Unique(versionFolders, FolderName($"{saved.ToLocalTime():yyyy-MM-dd HH.mm} {version.Label ?? pc}", version.Id.Value));
                        var files = new List<FileEntry>();
                        foreach (var file in version.Files)
                        {
                            ct.ThrowIfCancellationRequested();
                            var staged = Path.Combine(staging, Guid.NewGuid().ToString("N"));
                            await StageAsync(stream.Id, file, staged, restoring: false, ct);
                            if (ProgramFileDetector.IsProgramFile(staged))
                            {
                                notes.Add($"{title}: {file.Path} was left out: it's a program, not a save.");
                            }
                            else
                            {
                                // The entry takes its time from the file: the save's own, as the version records it.
                                File.SetLastWriteTimeUtc(staged, file.ModifiedUtc);
                                zip.CreateEntryFromFile(staged, $"{gameFolder}/{folder}/{file.Path}", CompressionLevel.Optimal);
                                files.Add(file);
                            }

                            File.Delete(staged);
                            done += file.Size;
                            progress?.Report((done, total));
                        }

                        shared.Add(new SharedVersion
                        {
                            Id = version.Id,
                            Folder = folder,
                            CreatedUtc = version.CreatedUtc,
                            SavedUtc = saved,
                            Pc = pc,
                            Label = version.Label,
                            Accounts = version.Accounts,
                            Files = files,
                        });
                    }

                    games.Add(new SharedGame
                    {
                        Id = stream.Id,
                        Title = title,
                        Folder = gameFolder,
                        Rules = versions.LastOrDefault(v => v.Rules is not null)?.Rules ?? PortableRules.From(stream.Game),
                        Versions = shared,
                    });
                }

                var manifest = new ShareManifest { GameSync = appVersion, PackedUtc = DateTime.UtcNow, PackedOn = Device.Name, Games = games };
                await using (var json = zip.CreateEntry(ShareManifest.FileName, CompressionLevel.Optimal).Open())
                {
                    await JsonSerializer.SerializeAsync(json, manifest, Json.Options, ct);
                }

                await using (var readme = new StreamWriter(zip.CreateEntry(ShareManifest.ReadmeName, CompressionLevel.Optimal).Open(), new UTF8Encoding(false)))
                {
                    await readme.WriteAsync(Readme(manifest, describeRoot));
                }
            }

            File.Move(part, zipPath);
        }
        catch
        {
            TryDeleteFile(part);
            throw;
        }
        finally
        {
            LocalHistory.TryDelete(staging);
        }

        foreach (var game in games)
        {
            _state.Log(game.Id, "info", $"Shared {Count(game.Versions.Count, "save")} in {Path.GetFileName(zipPath)}.", EventTags.Share);
        }

        return new SharePack(zipPath, games.Count, games.Sum(g => g.Versions.Count), new FileInfo(zipPath).Length, notes);
    }

    /// <summary>
    /// SHARE-10, SHARE-11: adds one game's saves from a shared zip, unpacked by version into <paramref name="versions"/>'
    /// folders, as pinned versions labelled as imported: never current, so nothing changes until one is restored. Every
    /// file gets a restore's checks first: a safe path (R6), the hash the zip says, no program (R1, left out and said so),
    /// and the antivirus (R3, which turns the whole save away). A save whose folders this PC's rules for the game don't
    /// have, or that's in its history already, isn't added, with the reason.
    /// </summary>
    public async Task<SharedImport> ImportSharedAsync(GameId game, IReadOnlyList<(SharedVersion Version, string Folder)> versions, DateTime nowLocal,
        CancellationToken ct)
    {
        var stream = Main(game);
        var definition = stream.Definition;
        await TryPullAsync(stream, ct);
        var (stored, _) = await LoadVersionsAsync(stream, ct);
        var known = stored.ToList();
        var notes = new List<string>();
        var (added, removed) = (0, 0);
        foreach (var (version, folder) in versions.OrderBy(v => v.Version.SavedUtc))
        {
            var name = version.Label is { } shown ? $"“{shown}”" : $"{version.Pc}'s save of {version.SavedUtc.ToLocalTime():d MMM HH:mm}";
            var label = version.Label is { } given ? $"Imported {nowLocal:d MMM yyyy}: {given}" : $"Imported {nowLocal:d MMM yyyy} from {version.Pc}";
            var missingRoot = version.Files.Select(f => f.Path.Split('/')[0]).Distinct(StringComparer.Ordinal).FirstOrDefault(r => !definition.Roots.ContainsKey(r));
            if (missingRoot is not null)
            {
                notes.Add($"{name} wasn't added: it was taken from a save folder ({missingRoot}) this PC's rules for {definition.Title} don't have.");
                continue;
            }

            var files = new List<FileEntry>();
            string? turnedAway = null;
            foreach (var file in version.Files)
            {
                ct.ThrowIfCancellationRequested();
                var path = Path.Combine(folder, file.Path.Replace('/', Path.DirectorySeparatorChar));
                try
                {
                    RestorePathGuard.CheckRelative(file.Path[(file.Path.IndexOf('/') + 1)..]);
                }
                catch (UnsafePathException e)
                {
                    turnedAway = $"{name} wasn't added: {e.Message}";
                    break;
                }

                if (!File.Exists(path) || await HashOfAsync(path, ct) != file.Hash)
                {
                    turnedAway = $"{name} wasn't added: {file.Path} in the zip isn't the file it was packed as.";
                    break;
                }

                if (ProgramFileDetector.IsProgramFile(path))
                {
                    removed++;
                    notes.Add($"{name}: {file.Path} was left out: it's a program, not a save.");
                    continue;
                }

                if (_malware.ScanFile(path) == ScanVerdict.Detected)
                {
                    turnedAway = $"{name} wasn't added: your antivirus flagged {file.Path}.";
                    break;
                }

                files.Add(file);
            }

            if (turnedAway is not null)
            {
                notes.Add(turnedAway);
                continue;
            }

            if (files.Count == 0)
            {
                notes.Add($"{name} wasn't added: no save files were left in it.");
                continue;
            }

            if (known.FirstOrDefault(v => FileSet.SameContent(v.Files, files)) is { } same)
            {
                notes.Add($"{name} is in its history already, as the version of {SavedAt(same).ToLocalTime():d MMM HH:mm}, so it wasn't added again.");
                continue;
            }

            var created = await UploadAsync(stream, files, VersionKind.Kept, VersionOrigin.Imported, null, [], pinned: false, label, ct,
                open: f => File.OpenRead(Path.Combine(folder, f.Path.Replace('/', Path.DirectorySeparatorChar))), sharedFrom: version.Pc);
            await _log.SetPinAsync(stream.Id, new PinRecord(created.Id, label, DateTime.UtcNow, Device, Named: false), ct);
            known.Add(created);
            added++;
        }

        if (added > 0)
        {
            _state.Log(stream.Id, "info", $"Imported {Count(added, "shared save")}, pinned in its history; none of them is current.", EventTags.Share);
            await AfterManualAsync(stream, new GameResult(stream.Id, definition.Title, SyncAction.Upload, StatusFor(stream), $"Imported {Count(added, "shared save")}."), ct);
        }

        return new SharedImport(added, removed, notes);
    }

    /// <summary>A shared zip's README (SHARE-08): how to import it, and how to restore a save by hand without GameSync.</summary>
    internal static string Readme(ShareManifest manifest, Func<string, string> describeRoot)
    {
        var text = new StringBuilder();
        text.AppendLine($"Game saves shared from GameSync {manifest.GameSync}, packed on {manifest.PackedUtc.ToLocalTime():d MMMM yyyy} on {manifest.PackedOn}.");
        text.AppendLine();
        text.AppendLine("With GameSync: open the save manager and choose Import saves. Each save joins its game's history, pinned,");
        text.AppendLine("and never replaces the save you have now; restore one from the game's saves when you want it.");
        text.AppendLine();
        text.AppendLine("By hand, without GameSync:");
        text.AppendLine("  1. Close the game.");
        text.AppendLine("  2. Copy the game's save folder somewhere safe first, so you can go back.");
        text.AppendLine("  3. Each save's folder in this zip holds one folder per place the game saves, named as below.");
        text.AppendLine("     Copy what's inside each one into that place, replacing the files there.");
        text.AppendLine();
        foreach (var game in manifest.Games)
        {
            text.AppendLine(game.Title);
            if (game.Rules is { } rules)
            {
                foreach (var (key, root) in rules.Roots.OrderBy(r => r.Key, StringComparer.Ordinal))
                {
                    text.AppendLine($"  {key}: {describeRoot(root)}");
                }
            }

            foreach (var version in game.Versions)
            {
                text.AppendLine($"  {game.Folder}/{version.Folder}  ({Count(version.Files.Count, "file")}, saved {version.SavedUtc.ToLocalTime():d MMM yyyy HH:mm} on {version.Pc})");
            }

            text.AppendLine();
        }

        text.AppendLine($"{ShareManifest.FileName} lists every file with its hash, for GameSync to check them on the way in.");
        return text.ToString();
    }

    /// <summary>A folder name a zip and every PC can take: no characters Windows refuses, no trailing dots or spaces, not too long.</summary>
    internal static string FolderName(string name, string fallback)
    {
        var bad = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':']).ToHashSet();
        var clean = new string(name.Select(c => bad.Contains(c) || char.IsControl(c) ? ' ' : c).ToArray());
        clean = string.Join(' ', clean.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim().TrimEnd('.', ' ');
        if (clean.Length > 80)
        {
            clean = clean[..80].TrimEnd('.', ' ');
        }

        return clean.Length == 0 || clean.All(c => c == '.') ? fallback : clean;
    }

    private static string Unique(HashSet<string> taken, string name)
    {
        var candidate = name;
        for (var n = 2; !taken.Add(candidate); n++)
        {
            candidate = $"{name} ({n})";
        }

        return candidate;
    }

    private static async Task<BlobId> HashOfAsync(string path, CancellationToken ct)
    {
        await using var file = File.OpenRead(path);
        return BlobId.FromHash(await SHA256.HashDataAsync(file, ct));
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A leftover part file is named as one, and is never taken for a zip.
        }
    }

    private static string Count(int count, string what) => count == 1 ? $"1 {what}" : $"{count} {what}s";
}
