using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GameSync.UI.ViewModels;

/// <summary>
/// SET-05 (design system version 57, SettingsScreen #report; a friend, 8 Oct 2026: "add a report a bug tool"): Report a
/// problem…, a dialog for what happened in the person's words, with the diagnostics added unless they untick them. Open on
/// GitHub opens a new issue on GameSync's GitHub with their words, and puts the diagnostics on the clipboard to paste in
/// (a link holds a couple of thousand characters, and the logs are far longer); nothing is posted until they submit it
/// there. The diagnostics never hold the sign-in or the saves.
/// </summary>
/// <param name="copy">Puts text on the clipboard.</param>
public sealed partial class ReportProblemViewModel(SettingsActions actions, Action<string> copy) : ObservableObject
{
    /// <summary>What happened, at most this long: the link to GitHub carries it.</summary>
    public const int MaxWords = 1000;

    /// <summary>The longest link Windows hands to the browser whole.</summary>
    private const int MaxLink = 1900;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpen))]
    private string _words = "";

    [ObservableProperty]
    private bool _addDiagnostics = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpen))]
    private bool _opening;

    /// <summary>What happened once GitHub opened: where the issue is, and what's on the clipboard to paste in.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDone), nameof(IsWriting))]
    private string? _done;

    public bool IsDone => Done is not null;

    public bool IsWriting => Done is null;

    public bool CanOpen => !Opening && Words.Trim().Length > 0;

    /// <summary>Why Open on GitHub waits, under the pointer.</summary>
    public string OpenTip => "Opens a new issue on GameSync's GitHub with what you wrote; you post it there";

    [RelayCommand]
    private async Task Open()
    {
        if (!CanOpen)
        {
            return;
        }

        Opening = true;
        try
        {
            string? diagnostics = null;
            if (AddDiagnostics)
            {
                try
                {
                    diagnostics = await actions.Diagnostics(CancellationToken.None);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    diagnostics = $"GameSync couldn't gather its diagnostics: {e.Message}";
                }
            }

            var (link, clipboard) = Link(actions.IssuesUrl, Words, diagnostics);
            if (clipboard is not null)
            {
                copy(clipboard);
            }

            actions.OpenLink(link);
            Done = clipboard is null
                ? "GitHub is opening in your browser with what you wrote. Check it there, then press Submit new issue."
                : diagnostics is not null
                    ? "GitHub is opening in your browser with what you wrote. The diagnostics are on your clipboard: paste them under Diagnostics (Ctrl+V), then press Submit new issue."
                    : "GitHub is opening in your browser. What you wrote is on your clipboard: paste it in (Ctrl+V), then press Submit new issue.";
        }
        finally
        {
            Opening = false;
        }
    }

    [RelayCommand]
    private void Close() => actions.CloseDialog();

    /// <summary>
    /// The new issue's link, with a title from the first line and the person's words in its body, and what goes on the
    /// clipboard: the diagnostics, or the whole report when the words make the link too long for Windows to pass on.
    /// </summary>
    public static (string Link, string? Clipboard) Link(string issuesUrl, string words, string? diagnostics)
    {
        words = words.Trim();
        var first = words.Split('\n', 2)[0].Trim();
        var title = first.Length == 0 ? "Problem report" : first.Length > 80 ? first[..80].TrimEnd() + "…" : first;
        var diagnosticsLine = diagnostics is null ? "" : $"{Environment.NewLine}{Environment.NewLine}### Diagnostics{Environment.NewLine}Paste them here (Ctrl+V): GameSync put them on your clipboard.";
        var body = $"### What happened{Environment.NewLine}{words}{diagnosticsLine}";
        var link = $"{issuesUrl}?title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}";
        if (link.Length <= MaxLink)
        {
            return (link, diagnostics);
        }

        // Too long for one link (words in a script whose every letter takes nine characters there): the report goes on the clipboard whole.
        var whole = diagnostics is null ? body : $"### What happened{Environment.NewLine}{words}{Environment.NewLine}{Environment.NewLine}### Diagnostics{Environment.NewLine}{diagnostics}";
        return ($"{issuesUrl}?title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString("Paste your report here (Ctrl+V): GameSync put it on your clipboard.")}", whole);
    }
}
