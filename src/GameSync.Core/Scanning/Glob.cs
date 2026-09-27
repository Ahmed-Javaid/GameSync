using System.Text;
using System.Text.RegularExpressions;

namespace GameSync.Core.Scanning;

/// <summary>
/// A path pattern relative to a rule's root, matched without case. <c>*</c> and <c>?</c> stay inside one folder;
/// <c>**</c> crosses folders, and <c>**/</c> also matches no folder at all.
/// </summary>
public sealed class Glob
{
    private readonly Regex _regex;

    public Glob(string pattern)
    {
        Pattern = pattern;
        _regex = new Regex(ToRegex(pattern), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public string Pattern { get; }

    public bool IsMatch(string relativePath) => _regex.IsMatch(relativePath);

    private static string ToRegex(string pattern)
    {
        var text = pattern.Replace('\\', '/').Trim('/');
        var regex = new StringBuilder("^");
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '*' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var folderPrefix = i + 2 < text.Length && text[i + 2] == '/';
                regex.Append(folderPrefix ? "(?:.*/)?" : ".*");
                i += folderPrefix ? 2 : 1;
            }
            else if (c == '*')
            {
                regex.Append("[^/]*");
            }
            else if (c == '?')
            {
                regex.Append("[^/]");
            }
            else
            {
                regex.Append(Regex.Escape(c.ToString()));
            }
        }

        return regex.Append('$').ToString();
    }
}

public static class DefaultExcludes
{
    /// <summary>Logs, crash dumps, shader caches and web caches never hold saves (FIND-11).</summary>
    public static readonly IReadOnlyList<Glob> All =
    [
        new("**/*.log"),
        new("**/*.log.*"),
        new("**/logs/**"),
        new("**/log/**"),
        new("**/*.dmp"),
        new("**/*.mdmp"),
        new("**/crashes/**"),
        new("**/crashdumps/**"),
        new("**/crashreports/**"),
        new("**/shadercache/**"),
        new("**/shaders_cache/**"),
        new("**/dxcache/**"),
        new("**/glcache/**"),
        new("**/gpucache/**"),
        new("**/d3dscache/**"),
        new("**/cache/**"),
        new("**/code cache/**"),
        new("**/*.tmp"),
    ];

    /// <summary>GameSync's own staged files from an interrupted restore.</summary>
    public static readonly Glob Staged = new("**/*.gs-new-*");
}
