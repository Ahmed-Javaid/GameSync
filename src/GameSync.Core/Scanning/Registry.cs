using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameSync.Core.Model;

namespace GameSync.Core.Scanning;

/// <summary>One registry value: its kind as Windows names it, and its data as text.</summary>
/// <param name="Kind">String, ExpandString, MultiString, DWord, QWord, Binary, or Raw for anything else, kept byte for byte.</param>
/// <param name="Data">A string as it is (ExpandString unexpanded), a number in decimal, binary and raw data in base64; null for MultiString.</param>
/// <param name="Lines">MultiString's strings.</param>
/// <param name="Type">Raw's registry type number, such as 4 for a REG_DWORD holding 8 bytes, which is how Unity keeps floats.</param>
public sealed record RegistryValue(string Kind, string? Data = null, IReadOnlyList<string>? Lines = null, int? Type = null);

/// <summary>A registry key's values and subkeys, sorted by name, so the same contents always export to the same bytes.</summary>
public sealed record RegistryNode
{
    public SortedDictionary<string, RegistryValue> Values { get; init; } = new(StringComparer.Ordinal);

    public SortedDictionary<string, RegistryNode> Keys { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>
/// FIND-10: registry keys under <c>HKEY_CURRENT_USER\Software</c>, read for backups and written only by a restore that
/// passed R7. Windows implements it; tests use a fake.
/// </summary>
public interface IRegistryStore
{
    /// <summary>The key with everything under it, and when anything in it last changed; null when it doesn't exist.</summary>
    (RegistryNode Node, DateTime ChangedUtc)? Export(string key);

    /// <summary>Makes the key hold exactly <paramref name="node"/>: values and subkeys it doesn't have are removed.</summary>
    void Import(string key, RegistryNode node);
}

/// <summary>A registry key as a file in a version: JSON naming the key, with its values and subkeys.</summary>
public static class RegistryFile
{
    public static byte[] Write(string key, RegistryNode node) =>
        JsonSerializer.SerializeToUtf8Bytes(new Contents { Key = key, Values = node.Values, Keys = node.Keys }, Json.Options);

    /// <summary>Reads an export. It came from a version, so it's checked as data only; a bad one throws <see cref="FormatException"/>.</summary>
    public static (string Key, RegistryNode Node) Read(byte[] bytes)
    {
        try
        {
            var contents = JsonSerializer.Deserialize<Contents>(bytes, Json.Options);
            if (contents?.Key is not { Length: > 0 } key)
            {
                throw new FormatException("A registry export names no key.");
            }

            return (key, Sorted(contents.Values, contents.Keys));
        }
        catch (JsonException e)
        {
            throw new FormatException($"A registry export can't be read: {e.Message}", e);
        }
    }

    /// <summary>Reading makes dictionaries with the default comparer; exports sort by ordinal.</summary>
    private static RegistryNode Sorted(IDictionary<string, RegistryValue>? values, IDictionary<string, RegistryNode>? keys) => new()
    {
        Values = new SortedDictionary<string, RegistryValue>(values ?? new Dictionary<string, RegistryValue>(), StringComparer.Ordinal),
        Keys = new SortedDictionary<string, RegistryNode>(
            (keys ?? new Dictionary<string, RegistryNode>()).ToDictionary(k => k.Key, k => Sorted(k.Value.Values, k.Value.Keys)),
            StringComparer.Ordinal),
    };

    /// <summary>The export's file name, the same on every PC: "hkey-current-user-software-studio-mdhr-cuphead.json".</summary>
    public static string FileName(string key)
    {
        var normalized = key.Replace('\\', '/').Trim('/').ToLowerInvariant();
        var slug = new StringBuilder();
        foreach (var c in normalized)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                slug.Append(c);
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        var name = slug.ToString().Trim('-');
        if (name.Length is 0 or > 80)
        {
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..8];
            name = $"{name[..Math.Min(name.Length, 70)].TrimEnd('-')}-{hash}".Trim('-');
        }

        return $"{name}.json";
    }

    private sealed class Contents
    {
        public string? Key { get; set; }

        public SortedDictionary<string, RegistryValue>? Values { get; set; }

        public SortedDictionary<string, RegistryNode>? Keys { get; set; }
    }
}
