using System.Globalization;
using System.Text.Json;
using GameSync.Core.Model;

namespace GameSync.Core.Discovery;

/// <summary>Where GameSync saw an achievement unlocked (design system version 54).</summary>
public enum UnlockSeen
{
    /// <summary>In Steam's own files on a PC (ACH-01).</summary>
    Steam,

    /// <summary>In the record a copy Steam doesn't run keeps itself (KAN-123).</summary>
    Record,

    /// <summary>Marked by the person, for a copy that writes its unlocks nowhere GameSync can find.</summary>
    Hand,
}

/// <summary>
/// One achievement in GameSync's own record (design system version 54; the owner, 8 Oct 2026: "The achievement system
/// needs to work even without steam. It can get information about WHAT achievements are there to begin with, but after
/// that it needs to work without it"): unlocked, when if that's known, where GameSync saw it and on which PC. A mark by
/// hand can be taken off again (<paramref name="Removed"/>, with when in <paramref name="ChangedUtc"/>); what a witness
/// saw, never.
/// </summary>
/// <param name="Id">Steam's own name for it, as its list has it.</param>
/// <param name="UnlockedUtc">When it was unlocked; null when the witness kept no time.</param>
/// <param name="Device">The PC it was seen on, by its device ID.</param>
/// <param name="SeenUtc">When GameSync first saw it there.</param>
/// <param name="ChangedUtc">When it was last marked or taken off; <paramref name="SeenUtc"/> for a witness's.</param>
public sealed record KeptUnlock(long AppId, string Id, DateTime? UnlockedUtc, UnlockSeen Seen, string Device, DateTime SeenUtc, DateTime ChangedUtc,
    bool Removed = false)
{
    /// <summary>Steam's files or a copy's own record saw it: it stays unlocked, whatever else is said of it.</summary>
    public bool Witnessed => Seen != UnlockSeen.Hand;
}

/// <summary>
/// GameSync's own record of achievements (design system version 54): how two entries for one achievement settle, the
/// same whichever PC meets them first, and the file each PC keeps in the cloud. Unlocks are only ever added, so the PCs'
/// records merge without conflicts: a witness's entry beats a mark by hand, the earliest unlock beats a later one, and
/// for marks the person's last word wins.
/// </summary>
public static class KeptAchievements
{
    /// <summary>The cloud file's shape; a file of another is ignored, not misread.</summary>
    public const int Format = 1;

    /// <summary>The largest cloud file read: a PC with a thousand games, each with a hundred unlocks, writes about 15 MB.</summary>
    public const int LargestFile = 32 * 1024 * 1024;

    /// <summary>The longest achievement name kept: Steam's are a few dozen characters.</summary>
    private const int LongestId = 200;

    /// <summary>The better of two entries for the same achievement: what every PC keeps, in whichever order they meet.</summary>
    public static KeptUnlock Better(KeptUnlock a, KeptUnlock b) => Compare(a, b) >= 0 ? a : b;

    /// <summary>
    /// Above 0 when <paramref name="a"/> is the better: a witness over a mark by hand (it's what the game wrote down);
    /// between witnesses, the earliest known unlock, a known day over none, then the first seen; between marks, the last
    /// change, taking one off winning a tie. Anything still level is settled by the entries' text, so every PC agrees.
    /// </summary>
    public static int Compare(KeptUnlock a, KeptUnlock b)
    {
        if (a.Witnessed != b.Witnessed)
        {
            return a.Witnessed ? 1 : -1;
        }

        if (a.Witnessed)
        {
            if (a.UnlockedUtc is { } x && b.UnlockedUtc is { } y && x != y)
            {
                return x < y ? 1 : -1;
            }

            if (a.UnlockedUtc is null != b.UnlockedUtc is null)
            {
                return a.UnlockedUtc is not null ? 1 : -1;
            }

            if (a.SeenUtc != b.SeenUtc)
            {
                return a.SeenUtc < b.SeenUtc ? 1 : -1;
            }
        }
        else
        {
            if (a.ChangedUtc != b.ChangedUtc)
            {
                return a.ChangedUtc > b.ChangedUtc ? 1 : -1;
            }

            if (a.Removed != b.Removed)
            {
                return a.Removed ? 1 : -1;
            }
        }

        return string.CompareOrdinal(Tie(a), Tie(b));
    }

    private static string Tie(KeptUnlock e) => string.Join('|', e.Device, e.Seen, e.UnlockedUtc?.Ticks, e.SeenUtc.Ticks, e.ChangedUtc.Ticks, e.Removed, e.Id);

    /// <summary>Every entry once per achievement, the better kept (names compared without regard to case).</summary>
    public static IReadOnlyList<KeptUnlock> Merge(IEnumerable<KeptUnlock> entries)
    {
        var kept = new Dictionary<(long, string), KeptUnlock>();
        foreach (var e in entries)
        {
            var key = (e.AppId, e.Id.ToUpperInvariant());
            kept[key] = kept.TryGetValue(key, out var old) ? Better(old, e) : e;
        }

        return kept.Values.ToList();
    }

    /// <summary>A PC's cloud file: <c>achievements/&lt;device&gt;.json</c>, every entry it keeps, marks taken off included.</summary>
    public static byte[] Write(DeviceId device, IEnumerable<KeptUnlock> entries, DateTime writtenUtc)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            json.WriteStartObject();
            json.WriteNumber("format", Format);
            json.WriteString("device", device.Value);
            json.WriteString("written", Text(writtenUtc));
            json.WriteStartArray("unlocks");
            foreach (var e in entries.OrderBy(e => e.AppId).ThenBy(e => e.Id, StringComparer.Ordinal))
            {
                json.WriteStartObject();
                json.WriteNumber("app", e.AppId);
                json.WriteString("id", e.Id);
                if (e.UnlockedUtc is { } at)
                {
                    json.WriteString("at", Text(at));
                }

                json.WriteString("by", Word(e.Seen));
                json.WriteString("device", e.Device);
                json.WriteString("seen", Text(e.SeenUtc));
                json.WriteString("changed", Text(e.ChangedUtc));
                if (e.Removed)
                {
                    json.WriteBoolean("removed", true);
                }

                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return stream.ToArray();
    }

    /// <summary>
    /// A PC's cloud file read back, every entry checked: one that doesn't hold up (a name out of reason, a time before 2000
    /// or in the future, a witness's entry taken off) is left out, and a file in another shape, or too large, gives none.
    /// </summary>
    /// <param name="nowUtc">This PC's clock: an entry dated more than two days ahead of it is left out.</param>
    public static (string? Device, IReadOnlyList<KeptUnlock> Entries) Read(ReadOnlySpan<byte> file, DateTime nowUtc)
    {
        if (file.Length > LargestFile)
        {
            return (null, []);
        }

        try
        {
            var reader = new Utf8JsonReader(file, new JsonReaderOptions { MaxDepth = 8 });
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.Number ||
                !format.TryGetInt32(out var f) || f != Format || !root.TryGetProperty("unlocks", out var unlocks) || unlocks.ValueKind != JsonValueKind.Array)
            {
                return (null, []);
            }

            var device = root.TryGetProperty("device", out var d) && d.ValueKind == JsonValueKind.String && IdRules.IsSafe(d.GetString(), 40) ? d.GetString() : null;
            var entries = new List<KeptUnlock>();
            foreach (var u in unlocks.EnumerateArray())
            {
                if (Entry(u, nowUtc) is { } entry)
                {
                    entries.Add(entry);
                }
            }

            return (device, entries);
        }
        catch (JsonException)
        {
            return (null, []);
        }
    }

    private static KeptUnlock? Entry(JsonElement u, DateTime nowUtc)
    {
        if (u.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? String(string name) => u.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
        DateTime? Time(string name) => String(name) is { } text && DateTime.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) && t >= Earliest && t <= nowUtc.AddDays(2) ? t : null;

        if (!u.TryGetProperty("app", out var appValue) || appValue.ValueKind != JsonValueKind.Number || !appValue.TryGetInt64(out var app) || app is <= 0 or > int.MaxValue ||
            String("id") is not { } id || !IsName(id) || Seen(String("by")) is not { } seen || String("device") is not { } device || !IdRules.IsSafe(device, 40) ||
            Time("seen") is not { } seenUtc || Time("changed") is not { } changedUtc)
        {
            return null;
        }

        var removed = u.TryGetProperty("removed", out var r) && r.ValueKind == JsonValueKind.True;
        var dated = u.TryGetProperty("at", out _);
        var at = dated ? Time("at") : null;
        if ((dated && at is null) || (removed && seen != UnlockSeen.Hand))
        {
            // A time that doesn't hold up, or a witness's unlock taken off: neither is GameSync's to keep.
            return null;
        }

        return new KeptUnlock(app, id, at, seen, device, seenUtc, changedUtc, removed);
    }

    /// <summary>An achievement's name as Steam's lists have them: printable, without line breaks, of a sane length.</summary>
    public static bool IsName(string? id) => id is { Length: > 0 and <= LongestId } && id.All(c => !char.IsControl(c)) && id.Trim().Length == id.Length;

    private static readonly DateTime Earliest = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static string Text(DateTime utc) => utc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    public static string Word(UnlockSeen seen) => seen switch
    {
        UnlockSeen.Steam => "steam",
        UnlockSeen.Record => "record",
        _ => "hand",
    };

    public static UnlockSeen? Seen(string? word) => word switch
    {
        "steam" => UnlockSeen.Steam,
        "record" => UnlockSeen.Record,
        "hand" => UnlockSeen.Hand,
        _ => null,
    };
}
