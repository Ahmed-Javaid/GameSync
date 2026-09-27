using System.Text;

namespace GameSync.Core.Discovery;

/// <summary>
/// Reads Valve's KeyValues text format (<c>libraryfolders.vdf</c>, <c>appmanifest_*.acf</c>, <c>loginusers.vdf</c>):
/// quoted keys with either a quoted value or a braced block. Keys compare without case, as Steam treats them.
/// </summary>
public sealed class Vdf
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Vdf> _blocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _order = [];

    public string? this[string key] => _values.GetValueOrDefault(key);

    public Vdf? Block(string key) => _blocks.GetValueOrDefault(key);

    /// <summary>Every nested block in file order, like the numbered libraries in <c>libraryfolders.vdf</c>.</summary>
    public IEnumerable<(string Key, Vdf Block)> Blocks() => _order.Where(_blocks.ContainsKey).Select(k => (k, _blocks[k]));

    public IEnumerable<(string Key, string Value)> Values() => _order.Where(_values.ContainsKey).Select(k => (k, _values[k]));

    /// <summary>The top level holds one named block, such as "AppState"; this returns that block.</summary>
    public static Vdf ParseRoot(string text) => Parse(text).Blocks().FirstOrDefault().Block ?? new Vdf();

    public static Vdf Parse(string text)
    {
        var at = 0;
        return ReadBlock(text, ref at, topLevel: true);
    }

    public static Vdf? TryReadRoot(string path)
    {
        try
        {
            return ParseRoot(File.ReadAllText(path, Encoding.UTF8));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            return null;
        }
    }

    private static Vdf ReadBlock(string text, ref int at, bool topLevel)
    {
        var block = new Vdf();
        while (true)
        {
            SkipSpaceAndComments(text, ref at);
            if (at >= text.Length)
            {
                return topLevel ? block : throw new FormatException("A block isn't closed.");
            }

            if (text[at] == '}')
            {
                at++;
                return topLevel ? throw new FormatException("A '}' closes nothing.") : block;
            }

            var key = ReadToken(text, ref at);
            SkipSpaceAndComments(text, ref at);
            if (at < text.Length && text[at] == '{')
            {
                at++;
                block._blocks[key] = ReadBlock(text, ref at, topLevel: false);
            }
            else
            {
                block._values[key] = ReadToken(text, ref at);
            }

            if (!block._order.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                block._order.Add(key);
            }
        }
    }

    private static string ReadToken(string text, ref int at)
    {
        if (at >= text.Length)
        {
            throw new FormatException("A value is missing.");
        }

        var token = new StringBuilder();
        if (text[at] != '"')
        {
            // Unquoted tokens run to the next whitespace or brace.
            while (at < text.Length && !char.IsWhiteSpace(text[at]) && text[at] is not ('{' or '}' or '"'))
            {
                token.Append(text[at++]);
            }

            return token.ToString();
        }

        at++;
        while (at < text.Length && text[at] != '"')
        {
            if (text[at] == '\\' && at + 1 < text.Length)
            {
                at++;
                token.Append(text[at] switch { 'n' => '\n', 't' => '\t', _ => text[at] });
                at++;
                continue;
            }

            token.Append(text[at++]);
        }

        if (at >= text.Length)
        {
            throw new FormatException("A quoted value isn't closed.");
        }

        at++;
        return token.ToString();
    }

    private static void SkipSpaceAndComments(string text, ref int at)
    {
        while (at < text.Length)
        {
            if (char.IsWhiteSpace(text[at]))
            {
                at++;
            }
            else if (text[at] == '/' && at + 1 < text.Length && text[at + 1] == '/')
            {
                while (at < text.Length && text[at] != '\n')
                {
                    at++;
                }
            }
            else
            {
                return;
            }
        }
    }
}
