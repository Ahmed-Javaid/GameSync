using System.Globalization;
using GameSync.Core.Discovery;
using GameSync.Core.Model;
using GameSync.Core.State;

namespace GameSync.Host;

public static partial class Cli
{
    /// <summary>
    /// FIND-04, learn mode from the command line: <c>gamesync learn</c> lists the games it waits for, watches or found
    /// places for; <c>learn &lt;game&gt;</c> shows one, its finds numbered; <c>--on</c> watches its next session, <c>--off</c>
    /// stops and forgets; <c>--add 1,2</c> adds those places, as Add a place does, and the game syncs. The watching itself
    /// is the background app's (or <c>gamesync agent</c>'s).
    /// </summary>
    private static async Task<int> LearnAsync(string dataDir, List<string> rest)
    {
        if (rest.Count == 0)
        {
            using var engine = Engine.Open(dataDir);
            var syncing = engine.Games.Select(g => g.Id).ToHashSet();
            var shown = 0;
            foreach (var entry in engine.Library.All().Where(e => e.MergedInto is null && e.State != LibraryState.Ignored).OrderBy(e => e.DisplayTitle, StringComparer.CurrentCultureIgnoreCase))
            {
                var view = LearnMode.View(dataDir, engine.State, entry, syncing.Contains(entry.Id));
                if (view.State is LearnState.Off or LearnState.AntiCheat)
                {
                    continue;
                }

                Console.WriteLine($"  {entry.DisplayTitle,-36} {Words(view)}   ({entry.Id})");
                shown++;
            }

            Console.WriteLine(shown == 0
                ? "Learn mode isn't waiting for any game: every game here has its saves found, or it's off for them."
                : "Learn mode watches each of these the next time it's played. 'gamesync learn <game>' shows what it found.");
            return 0;
        }

        var game = Game(rest, 0);
        var add = TakeOption(rest, "--add");
        if (rest.Contains("--on") || rest.Contains("--off"))
        {
            LearnMode.Set(dataDir, game, rest.Contains("--on"));
            Console.WriteLine(rest.Contains("--on")
                ? $"Learn mode watches {game} the next time it's played, with GameSync running (the tray app, or 'gamesync agent')."
                : $"Learn mode is off for {game}, and what it found is forgotten. 'gamesync learn {game} --on' turns it on again.");
            return 0;
        }

        var finds = LearnMode.ReadFinds(dataDir, game);
        if (add is not null)
        {
            if (finds is not { Places.Count: > 0 })
            {
                throw new UsageException($"Learn mode hasn't found anything for {game} yet.");
            }

            var picked = new List<string>();
            foreach (var part in add.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n < 1 || n > finds.Places.Count)
                {
                    throw new UsageException($"There's no place {part}: pick from 1 to {finds.Places.Count.ToString(CultureInfo.InvariantCulture)}, as 'gamesync learn {game}' numbers them.");
                }

                picked.Add(finds.Places[n - 1].Path);
            }

            Console.WriteLine(await LearnMode.AddAsync(dataDir, game, picked, () => Console.WriteLine("Waiting for the sync in the background to finish first."),
                CancellationToken.None));
            Console.WriteLine($"Sync it now with: gamesync sync {game}");
            return 0;
        }

        using (var state = new StateStore(dataDir))
        using (var library = new LibraryStore(dataDir))
        {
            var entry = library.All().FirstOrDefault(e => e.Id == game && e.MergedInto is null)
                ?? throw new UsageException($"GameSync doesn't know a game called {game}. 'gamesync games' lists them.");
            var syncs = AppConfig.Load(dataDir)?.Games.Any(g => g.Id == game) == true || entry.State == LibraryState.Synced;
            var view = LearnMode.View(dataDir, state, entry, syncs);
            Console.WriteLine($"{entry.DisplayTitle}: {Words(view)}");
        }

        if (finds is { Places.Count: > 0 })
        {
            Console.WriteLine();
            for (var i = 0; i < finds.Places.Count; i++)
            {
                var place = finds.Places[i];
                Console.WriteLine($"  {i + 1}. {place.Portable}");
                Console.WriteLine($"     {(place.IsFile ? "one file" : $"{place.Files} file{(place.Files == 1 ? "" : "s")}")}, {FormatSize(place.Bytes)}, newest {place.NewestUtc.ToLocalTime():d MMM HH:mm}" +
                    $"{(place.Examples.Count > 0 && !place.IsFile ? $": {string.Join(", ", place.Examples)}" : "")}{(place.Tags.Count > 0 ? $"   [{string.Join("; ", place.Tags)}]" : "")}");
            }

            Console.WriteLine();
            Console.WriteLine($"Nothing syncs until you pick: gamesync learn {game} --add 1   (or 1,2 for more than one)");
        }

        return 0;
    }

    /// <summary>Learn mode for a game, in a few words.</summary>
    private static string Words(LearnView view) => view.State switch
    {
        LearnState.Waiting => "watches the next time it's played",
        LearnState.Watching => $"watching now, since {view.WatchingSinceUtc?.ToLocalTime():HH:mm}",
        LearnState.Found => $"found {view.Finds!.Places.Count} place{(view.Finds.Places.Count == 1 ? "" : "s")} it saved to, waiting for you to pick",
        LearnState.NothingFound => "watched, saw nothing like a save; watches again next time",
        LearnState.AntiCheat => "never: it ships an anti-cheat (R15)",
        _ => "off",
    };
}
