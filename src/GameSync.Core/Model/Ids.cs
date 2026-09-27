using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameSync.Core.Model;

/// <summary>A validated string id. Ids become file names on every storage backend, so they only allow safe characters.</summary>
public interface IStringId<TSelf> where TSelf : struct, IStringId<TSelf>
{
    string Value { get; }

    static abstract TSelf Parse(string value);
}

public sealed class StringIdJsonConverter<T> : JsonConverter<T> where T : struct, IStringId<T>
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString() ?? throw new JsonException($"Expected a string for {typeof(T).Name}.");
        try
        {
            return T.Parse(text);
        }
        catch (FormatException e)
        {
            throw new JsonException(e.Message, e);
        }
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
}

internal static class IdRules
{
    public static bool IsSafe(string? value, int maxLength) =>
        !string.IsNullOrEmpty(value) && value.Length <= maxLength && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}

/// <summary>A game, or one of its per-PC streams (settings and screenshots): letters, digits, '-' and '_'.</summary>
[JsonConverter(typeof(StringIdJsonConverter<GameId>))]
public readonly record struct GameId : IStringId<GameId>
{
    private GameId(string value) => Value = value;

    public string Value { get; }

    public static GameId Parse(string value) =>
        IdRules.IsSafe(value, 64) ? new GameId(value) : throw new FormatException($"'{value}' isn't a valid game id (letters, digits, '-' and '_', up to 64).");

    public override string ToString() => Value;
}

/// <summary>A version: sortable by time, for example <c>2026-09-27T21-04-10Z_DESKTOP_3f2a9c</c>.</summary>
[JsonConverter(typeof(StringIdJsonConverter<VersionId>))]
public readonly record struct VersionId : IStringId<VersionId>, IComparable<VersionId>
{
    private VersionId(string value) => Value = value;

    public string Value { get; }

    public static VersionId Parse(string value) =>
        IdRules.IsSafe(value, 100) ? new VersionId(value) : throw new FormatException($"'{value}' isn't a valid version id.");

    public static VersionId New(DateTime utcNow, string deviceName)
    {
        var device = new string(deviceName.Where(char.IsAsciiLetterOrDigit).Take(24).ToArray());
        if (device.Length == 0)
        {
            device = "PC";
        }

        var stamp = utcNow.ToString("yyyy-MM-dd'T'HH-mm-ss'Z'", CultureInfo.InvariantCulture);
        var suffix = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(3));
        return Parse($"{stamp}_{device}_{suffix}");
    }

    public int CompareTo(VersionId other) => string.CompareOrdinal(Value, other.Value);

    public override string ToString() => Value;
}

/// <summary>The SHA-256 of a file's contents, in lowercase hex. Each unique file is stored once under it.</summary>
[JsonConverter(typeof(StringIdJsonConverter<BlobId>))]
public readonly record struct BlobId : IStringId<BlobId>
{
    private BlobId(string value) => Value = value;

    public string Value { get; }

    public static BlobId Parse(string value) =>
        value is { Length: 64 } && value.All(char.IsAsciiHexDigitLower) ? new BlobId(value) : throw new FormatException($"'{value}' isn't a SHA-256 hash.");

    public static BlobId FromHash(ReadOnlySpan<byte> sha256) => new(Convert.ToHexStringLower(sha256));

    public override string ToString() => Value;
}

[JsonConverter(typeof(StringIdJsonConverter<DeviceId>))]
public readonly record struct DeviceId : IStringId<DeviceId>
{
    private DeviceId(string value) => Value = value;

    public string Value { get; }

    public static DeviceId Parse(string value) =>
        IdRules.IsSafe(value, 40) ? new DeviceId(value) : throw new FormatException($"'{value}' isn't a valid device id.");

    public static DeviceId New() => new("d-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8)));

    public override string ToString() => Value;
}
