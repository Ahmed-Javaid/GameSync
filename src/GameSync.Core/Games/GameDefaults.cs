using System.Text.Json;
using GameSync.Core.Model;
using GameSync.Core.State;

namespace GameSync.Core.Games;

/// <summary>
/// SET-06: what every new game starts from, as Settings → Backup and sync → What to back up sets it on this PC: game
/// saves always; settings files synced between PCs, backed up on this PC, or not backed up; screenshots; logs, crash
/// dumps and caches skipped; patterns no game should back up; and who wins when two PCs disagree. A game keeps what it
/// started with: changing a default changes the games already syncing only when the person applies it to them.
/// </summary>
public sealed record GameDefaults
{
    public const string SyncBetween = "sync";
    public const string ThisPc = "this-pc";
    public const string Off = "off";

    /// <summary>A pattern is at most this long, and there are at most this many.</summary>
    public const int PatternLength = 200;
    public const int Patterns = 50;

    private const string Prefix = "defaults.";

    /// <summary>Settings files: <see cref="SyncBetween"/>, <see cref="ThisPc"/> (the default, FIND-09) or <see cref="Off"/>.</summary>
    public string SettingsFiles { get; init; } = ThisPc;

    /// <summary>Screenshots a game saves in its own folders, backed up on this PC.</summary>
    public bool Screenshots { get; init; }

    /// <summary>Skip logs, crash dumps and caches (FIND-11).</summary>
    public bool SkipJunk { get; init; } = true;

    /// <summary>Files or folders no game backs up, as the person wrote them: <c>*.bak</c>, <c>**/Mods/**</c>.</summary>
    public IReadOnlyList<string> Skip { get; init; } = [];

    public ConflictPolicy Conflict { get; init; } = ConflictPolicy.NewestWins;

    public static GameDefaults Load(StateStore state)
    {
        var settings = state.GetSetting(Prefix + "settingsFiles");
        IReadOnlyList<string> skip = [];
        if (state.GetSetting(Prefix + "skip") is { Length: > 0 } text)
        {
            try
            {
                skip = (JsonSerializer.Deserialize<List<string>>(text) ?? []).Where(p => Refusal(p) is null).Distinct(StringComparer.OrdinalIgnoreCase).Take(Patterns).ToList();
            }
            catch (JsonException)
            {
                // A damaged list reads as none; Settings shows it empty and saving writes it again.
            }
        }

        return new GameDefaults
        {
            SettingsFiles = settings is SyncBetween or Off ? settings : ThisPc,
            Screenshots = state.GetSetting(Prefix + "screenshots") == "1",
            SkipJunk = state.GetSetting(Prefix + "skipJunk") != "0",
            Skip = skip,
            Conflict = state.GetSetting(Prefix + "conflict") == "ask" ? ConflictPolicy.AlwaysAsk : ConflictPolicy.NewestWins,
        };
    }

    public void Save(StateStore state)
    {
        state.SetSetting(Prefix + "settingsFiles", SettingsFiles);
        state.SetSetting(Prefix + "screenshots", Screenshots ? "1" : "0");
        state.SetSetting(Prefix + "skipJunk", SkipJunk ? "1" : "0");
        state.SetSetting(Prefix + "skip", JsonSerializer.Serialize(Skip));
        state.SetSetting(Prefix + "conflict", Conflict == ConflictPolicy.AlwaysAsk ? "ask" : "newest");
    }

    /// <summary>
    /// Why a pattern can't be one to skip: empty, too long, a whole path, leaving the game's folder, or one that would
    /// skip every file (a game with nothing backed up looks the same as a game with no saves). Null when it's fine.
    /// </summary>
    public static string? Refusal(string pattern)
    {
        var text = pattern.Trim().Replace('\\', '/');
        if (text.Length == 0)
        {
            return "Write a pattern, such as *.bak or **/Mods/**.";
        }

        if (text.Length > PatternLength)
        {
            return $"A pattern is at most {PatternLength} characters.";
        }

        if (text.Contains(':') || text.StartsWith('/') || text.Split('/').Any(part => part == ".."))
        {
            return "A pattern names files inside a game's save folders, such as *.bak, not a place on the PC.";
        }

        if (text.Any(c => char.IsControl(c) || c is '<' or '>' or '|' or '"'))
        {
            return "A pattern can't hold < > | or \", which no file name has.";
        }

        return text.Trim('/').Split('/').All(part => part.Length > 0 && part.All(c => c == '*'))
            ? "That pattern would skip every file, so no game would be backed up. Name the files to skip, such as *.bak."
            : null;
    }

    /// <summary>
    /// A pattern as a rule's exclude: one with no folder in it skips that name in any folder, as <c>*.bak</c> skips
    /// <c>slot1.bak</c> and <c>old/slot1.bak</c>; one with a folder is taken as written, from the save folder's top.
    /// </summary>
    public static string Exclude(string pattern)
    {
        var text = pattern.Trim().Replace('\\', '/').Trim('/');
        return text.Contains('/') ? text : $"**/{text}";
    }

    /// <summary>
    /// A new game's definition, as the defaults say. Rules for whole folders take the skips; a rule for one particular
    /// file keeps it, since that file was chosen.
    /// </summary>
    public GameDefinition Apply(GameDefinition game) => game with
    {
        SyncConfig = SettingsFiles == SyncBetween,
        IncludeScreenshots = Screenshots,
        ConflictPolicy = Conflict,
        Rules = game.Rules.Select(Apply).ToList(),
    };

    private SaveRule Apply(SaveRule rule)
    {
        var excludes = rule.Exclude.ToList();
        if (rule.Category == SaveCategory.Config && SettingsFiles == Off && !excludes.Contains("**"))
        {
            excludes.Add("**");
        }

        var folder = rule.Root != GameDefinition.RegistryRoot && WholeFolder(rule);
        if (folder)
        {
            excludes.AddRange(Skip.Select(Exclude).Where(e => !excludes.Contains(e, StringComparer.OrdinalIgnoreCase)));
        }

        return rule with { Exclude = excludes, UseDefaultExcludes = folder ? SkipJunk : rule.UseDefaultExcludes };
    }

    /// <summary>Whether a game already syncing takes what these defaults would give it, in its files; its conflict choice is <see cref="FollowsConflict"/>.</summary>
    public bool FollowsFiles(GameDefinition game)
    {
        if (game.Rules.Any(r => r.Category == SaveCategory.Config))
        {
            var configOff = game.Rules.Where(r => r.Category == SaveCategory.Config).All(r => r.Exclude.Contains("**"));
            var chosen = configOff ? Off : game.SyncConfig ? SyncBetween : ThisPc;
            if (chosen != SettingsFiles)
            {
                return false;
            }
        }

        if (game.Rules.Any(r => r.Category == SaveCategory.Screenshots) && game.IncludeScreenshots != Screenshots)
        {
            return false;
        }

        var folders = game.Rules.Where(r => r.Root != GameDefinition.RegistryRoot && WholeFolder(r)).ToList();
        return folders.All(r => r.UseDefaultExcludes == SkipJunk) &&
               Skip.Select(Exclude).All(e => folders.All(r => r.Exclude.Contains(e, StringComparer.OrdinalIgnoreCase)));
    }

    public bool FollowsConflict(GameDefinition game) => game.ConflictPolicy == Conflict;

    /// <summary>
    /// A game already syncing, changed to take these defaults' files: settings files and screenshots as they say, logs
    /// and caches skipped or not, and every pattern skipped. Nothing it backs up by its own choice is taken away but
    /// what a pattern names; patterns dropped from the defaults stay on the games that took them.
    /// </summary>
    public GameDefinition ApplyFiles(GameDefinition game) => game with
    {
        SyncConfig = SettingsFiles == SyncBetween,
        IncludeScreenshots = Screenshots,
        Rules = game.Rules.Select(r =>
        {
            var excludes = r.Exclude.ToList();
            if (r.Category == SaveCategory.Config)
            {
                excludes.RemoveAll(e => e == "**");
                if (SettingsFiles == Off)
                {
                    excludes.Add("**");
                }
            }

            if (r.Root != GameDefinition.RegistryRoot && WholeFolder(r))
            {
                excludes.AddRange(Skip.Select(Exclude).Where(e => !excludes.Contains(e, StringComparer.OrdinalIgnoreCase)));
                return r with { Exclude = excludes, UseDefaultExcludes = SkipJunk };
            }

            return r with { Exclude = excludes };
        }).ToList(),
    };

    /// <summary>A rule for a whole folder, or files by a pattern, rather than one particular file.</summary>
    private static bool WholeFolder(SaveRule rule) => rule.Include == "**" || rule.Include.Contains('*');
}
