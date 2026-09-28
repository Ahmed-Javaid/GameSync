// A stand-in game for testing the watcher and session timing without a real game: it writes a save file at the times
// given, then exits. Copied into a folder under a game's name, it runs as that game. Started with nothing, as a
// launcher starts a game, it runs for a second and writes nothing.
//
//   FakeGame [--save <file> [--write-at <seconds>]... [--text <text>]] [--run <seconds>]

using System.Diagnostics;
using System.Globalization;

var save = Option("--save");
var text = Option("--text") ?? "a save";
var run = double.Parse(Option("--run") ?? (save is null ? "1" : "5"), CultureInfo.InvariantCulture);
var writes = Options("--write-at").Select(s => double.Parse(s, CultureInfo.InvariantCulture)).Order().ToList();

var clock = Stopwatch.StartNew();
foreach (var at in save is null ? [] : writes)
{
    Wait(at);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(save!))!);
    File.WriteAllText(save!, $"{text} at {at.ToString(CultureInfo.InvariantCulture)} s");
}

Wait(run);
return 0;

void Wait(double seconds)
{
    var left = TimeSpan.FromSeconds(seconds) - clock.Elapsed;
    if (left > TimeSpan.Zero)
    {
        Thread.Sleep(left);
    }
}

string? Option(string name) => Options(name).LastOrDefault();

IEnumerable<string> Options(string name)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == name)
        {
            yield return args[i + 1];
        }
    }
}
