using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace GameSync.Core.Discovery;

/// <summary>One of a game's achievements as Steam lists it, and when this PC's player unlocked it.</summary>
/// <param name="Id">Steam's own name for it (<c>ACH_WIN_ONE_GAME</c>), the same in every language.</param>
/// <param name="Hidden">Steam keeps its name and description secret until it's unlocked.</param>
/// <param name="Icon">The icon's file on Steam's image server once unlocked; <paramref name="IconLocked"/> before.</param>
/// <param name="UnlockedUtc">When it was unlocked; null while it's locked.</param>
public sealed record SteamAchievement(string Id, string Name, string? Description, bool Hidden, string? Icon, string? IconLocked, DateTime? UnlockedUtc)
{
    public bool Unlocked => UnlockedUtc is not null;
}

/// <summary>A Steam game's achievements on this PC, for the account that played it last.</summary>
public sealed record SteamGameAchievements(long AppId, IReadOnlyList<SteamAchievement> All)
{
    public int Unlocked => All.Count(a => a.Unlocked);
}

/// <summary>
/// ACH-01: a Steam game's achievements read from the Steam client's own files on this PC, with no key and offline:
/// <c>appcache\stats\UserGameStatsSchema_&lt;app&gt;.bin</c> lists them (names, descriptions, hidden, icons) and
/// <c>UserGameStats_&lt;account&gt;_&lt;app&gt;.bin</c> holds which are unlocked and when, for each account that has played
/// it; the one written last is taken. Both are Valve's binary KeyValues. Read only; nothing is sent anywhere.
/// </summary>
public static class SteamAchievements
{
    /// <summary>Null when Steam has no list of achievements for the game on this PC (none, or never run here).</summary>
    public static SteamGameAchievements? Read(string steamRoot, long appId)
    {
        var app = appId.ToString(CultureInfo.InvariantCulture);
        var stats = Path.Combine(steamRoot, "appcache", "stats");
        var schemaFile = Path.Combine(stats, $"UserGameStatsSchema_{app}.bin");
        try
        {
            if (!File.Exists(schemaFile) || BinaryKeyValues.Parse(File.ReadAllBytes(schemaFile)) is not { } schema ||
                (schema.Block(app) ?? schema.Blocks().FirstOrDefault().Block)?.Block("stats") is not { } statsBlock)
            {
                return null;
            }

            var progress = new DirectoryInfo(stats).EnumerateFiles($"UserGameStats_*_{app}.bin").MaxBy(f => f.LastWriteTimeUtc);
            var user = progress is null ? null : BinaryKeyValues.Parse(File.ReadAllBytes(progress.FullName));
            user = user?.Block("cache") ?? user;

            var all = new List<SteamAchievement>();
            foreach (var (statId, stat) in statsBlock.Blocks())
            {
                if (stat.Block("bits") is not { } bits || !(stat["type"] is "ACHIEVEMENTS" or "4" or "5"))
                {
                    continue;
                }

                var mine = user?.Block(statId);
                var unlockedBits = unchecked((uint)(mine?.Number("data") ?? 0));
                var times = mine?.Block("AchievementTimes");
                foreach (var (bitKey, entry) in bits.Blocks())
                {
                    if (!int.TryParse(bitKey, NumberStyles.None, CultureInfo.InvariantCulture, out var bit) || bit > 31)
                    {
                        continue;
                    }

                    var display = entry.Block("display");
                    var unlocked = (unlockedBits & (1u << bit)) != 0;
                    var at = times?.Number(bitKey) is { } seconds and > 0 ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime : (DateTime?)null;
                    all.Add(new SteamAchievement(
                        entry["name"] ?? $"{statId}.{bitKey}",
                        English(display, "name") ?? entry["name"] ?? "",
                        English(display, "desc"),
                        display?["hidden"] == "1",
                        display?["icon"],
                        display?["icon_gray"],
                        unlocked ? at ?? DateTime.UnixEpoch : null));
                }
            }

            return all.Count == 0 ? null : new SteamGameAchievements(appId, all);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            // A file Steam is writing, or one in a shape this doesn't know: no achievements rather than wrong ones.
            return null;
        }
    }

    /// <summary>A display text in English, as Steam keeps it: one string, or one per language.</summary>
    private static string? English(BinaryKeyValues? display, string key) =>
        display is null ? null
        : display[key] is { Length: > 0 } plain ? plain
        : display.Block(key) is { } languages ? languages["english"] ?? languages.Values().Select(v => v.Value).FirstOrDefault(v => v.Length > 0)
        : null;
}

/// <summary>
/// Valve's binary KeyValues, as the Steam client keeps its caches: a type byte, a key ending in a zero byte, then the
/// value (a nested block, a text, a 32-bit or 64-bit number, a float), and the byte 8 ending each block.
/// </summary>
public sealed class BinaryKeyValues
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _numbers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BinaryKeyValues> _blocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _order = [];

    /// <summary>A text value, or a number as text.</summary>
    public string? this[string key] =>
        _values.GetValueOrDefault(key) ?? (_numbers.TryGetValue(key, out var n) ? n.ToString(CultureInfo.InvariantCulture) : null);

    public long? Number(string key) =>
        _numbers.TryGetValue(key, out var n) ? n
        : long.TryParse(_values.GetValueOrDefault(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed
        : null;

    public BinaryKeyValues? Block(string key) => _blocks.GetValueOrDefault(key);

    public IEnumerable<(string Key, BinaryKeyValues Block)> Blocks() => _order.Where(_blocks.ContainsKey).Select(k => (k, _blocks[k]));

    public IEnumerable<(string Key, string Value)> Values() => _order.Where(_values.ContainsKey).Select(k => (k, _values[k]));

    public static BinaryKeyValues Parse(ReadOnlySpan<byte> data)
    {
        var at = 0;
        return ReadBlock(data, ref at);
    }

    private static BinaryKeyValues ReadBlock(ReadOnlySpan<byte> data, ref int at)
    {
        var block = new BinaryKeyValues();
        while (at < data.Length)
        {
            var type = data[at++];
            if (type == 8)
            {
                return block;
            }

            var key = ReadString(data, ref at);
            block._order.Add(key);
            switch (type)
            {
                case 0:
                    block._blocks[key] = ReadBlock(data, ref at);
                    break;
                case 1:
                    block._values[key] = ReadString(data, ref at);
                    break;
                case 2 or 4 or 6:
                    block._numbers[key] = BinaryPrimitives.ReadInt32LittleEndian(Take(data, ref at, 4));
                    break;
                case 3:
                    block._values[key] = BinaryPrimitives.ReadSingleLittleEndian(Take(data, ref at, 4)).ToString(CultureInfo.InvariantCulture);
                    break;
                case 7 or 10:
                    block._numbers[key] = BinaryPrimitives.ReadInt64LittleEndian(Take(data, ref at, 8));
                    break;
                default:
                    throw new FormatException($"Unknown KeyValues type {type} at byte {at}.");
            }
        }

        return block;
    }

    private static ReadOnlySpan<byte> Take(ReadOnlySpan<byte> data, ref int at, int length)
    {
        if (at + length > data.Length)
        {
            throw new FormatException("The KeyValues data ends part way through a value.");
        }

        var slice = data.Slice(at, length);
        at += length;
        return slice;
    }

    private static string ReadString(ReadOnlySpan<byte> data, ref int at)
    {
        var end = data[at..].IndexOf((byte)0);
        if (end < 0)
        {
            throw new FormatException("The KeyValues data ends part way through a text.");
        }

        var text = Encoding.UTF8.GetString(data.Slice(at, end));
        at += end + 1;
        return text;
    }
}
