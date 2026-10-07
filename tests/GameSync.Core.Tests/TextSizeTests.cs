using System.Text.RegularExpressions;
using Avalonia.Controls;
using GameSync.UI.Theming;

namespace GameSync.Core.Tests;

/// <summary>A11Y-04: GameSync's text follows Windows' text size.</summary>
public partial class TextSizeTests
{
    [GeneratedRegex(@"DynamicResource (fs|lh|cw|dw)-(\d+)\}")]
    private static partial Regex SizeResource();

    [Fact]
    public void A11Y_04_every_text_size_the_screens_use_is_in_the_scale()
    {
        // A size missing from the scale would quietly fall back to the default and never follow Windows.
        var ui = Path.Combine(RepoRoot(), "src", "GameSync.UI");
        var used = Directory.EnumerateFiles(ui, "*.axaml", SearchOption.AllDirectories)
            .SelectMany(f => SizeResource().Matches(File.ReadAllText(f)))
            .Select(m => (Kind: m.Groups[1].Value, Size: int.Parse(m.Groups[2].Value)))
            .Distinct()
            .ToList();
        var known = new Dictionary<string, IReadOnlyList<int>>
        {
            ["fs"] = TextScale.Sizes,
            ["lh"] = TextScale.LineHeights,
            ["cw"] = TextScale.TextColumns,
            ["dw"] = TextScale.DialogWidths,
        };

        Assert.True(used.Count > 20, "The screens' sizes are resources.");
        Assert.DoesNotContain(used, u => !known[u.Kind].Contains(u.Size));

        // And no fixed text size is left, apart from a title cover's big letter, which is part of the art.
        var fixedSizes = Directory.EnumerateFiles(ui, "*.axaml", SearchOption.AllDirectories)
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"(?<![\w.])(FontSize|LineHeight)=""(\d+)""|Property=""(?:\w+\.)?(FontSize|LineHeight)"" Value=""(\d+)""").Select(m => $"{Path.GetFileName(f)}: {m.Value}"))
            .Where(m => !m.EndsWith("\"128\"", StringComparison.Ordinal))
            .ToList();
        Assert.Empty(fixedSizes);
    }

    [Fact]
    public void A11Y_04_at_150_percent_text_is_half_as_big_again_and_the_columns_and_dialogs_widen_less()
    {
        var resources = new ResourceDictionary();
        try
        {
            Assert.True(TextScale.Apply(resources, 1.5));
            Assert.Equal(19.5, resources["fs-13"]);
            Assert.Equal(28.5, resources["lh-19"]);
            Assert.Equal(21.0, resources["ControlContentThemeFontSize"]);
            Assert.Equal(new GridLength(354), resources["col-library-list"]);
            Assert.Equal(new GridLength(222), resources["cw-148"]);
            Assert.Equal(832.0, resources["dw-640"]);

            // The largest Windows allows: dialogs stop at 30% wider, so Properties still fits a 1280 window.
            TextScale.Apply(resources, 3);
            Assert.Equal(13 * 2.25, resources["fs-13"]);
            Assert.Equal(1144.0, resources["dw-880"]);
            Assert.False(TextScale.Apply(resources, 2.25));
        }
        finally
        {
            TextScale.Apply(resources, 1);
        }
    }

    private static string RepoRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "GameSync.sln")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new InvalidOperationException("The tests run outside the repository.");
    }
}
