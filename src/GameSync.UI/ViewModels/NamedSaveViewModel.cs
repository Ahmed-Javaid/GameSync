using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameSync.Core.Model;

namespace GameSync.UI.ViewModels;

/// <summary>
/// What New named save… opens with: the game, its save as it is now (its main place on this PC and what's there), the
/// names its named saves have, and, for a game not syncing yet, what keeping it starts with (KAN-63).
/// </summary>
public sealed record NamedSaveStart(GameId Id, string Title, string? Folder, string? Meta, IReadOnlyList<string> Names, string? KeepLine)
{
    /// <summary>
    /// From a game's page or its saves: the first place on this PC that holds its saves (else any), with what's there,
    /// and how many more places the save takes in.
    /// </summary>
    public static NamedSaveStart For(GameId id, string title, IEnumerable<(string? Folder, string? Tag, string Evidence)> places, IEnumerable<string> names,
        string? keepLine)
    {
        var here = places.Where(p => p.Folder is not null).ToList();
        var main = here.FirstOrDefault(p => p.Tag is null);
        main = main.Folder is null ? here.FirstOrDefault() : main;
        var more = here.Count - (main.Folder is null ? 0 : 1);
        var meta = main.Folder is null ? null
            : more > 0 ? $"{main.Evidence} · and {(more == 1 ? "1 more place" : $"{more.ToString(CultureInfo.InvariantCulture)} more places")}"
            : main.Evidence;
        return new NamedSaveStart(id, title, main.Folder, meta, names.ToList(), keepLine);
    }
}

/// <summary>
/// New named save (design system → NamedSaveDialog; BAK-18, KAN-77, the owner: like Add a place, not just a grey box):
/// the game's save as it is now, kept under a name on every PC. Over the page: what's kept (where, how many files, how
/// big, how new), the name, and Keep this save, which waits until the save is kept and then closes, or says why it
/// couldn't be. A name the game has already is turned away, since two saves under one name couldn't be told apart.
/// </summary>
public sealed partial class NamedSaveViewModel(NamedSaveStart start, LauncherActions? actions) : ObservableObject
{
    /// <summary>The longest name a save takes, as the engine's.</summary>
    public const int MaxName = 100;

    public GameId Id => start.Id;

    public string Subtitle => $"{start.Title}'s save as it is now, kept under a name";

    public string? Folder => start.Folder;

    public bool HasSave => start.Folder is not null;

    public string? Meta => start.Meta;

    /// <summary>A game not syncing yet: GameSync starts keeping its saves, backed up only (KAN-63).</summary>
    public string? KeepLine => start.KeepLine;

    public bool HasKeepLine => start.KeepLine is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Taken), nameof(HasTaken), nameof(CanKeep), nameof(KeepTip))]
    private string _name = "";

    /// <summary>On its way: Keep this save reads Keeping…, and the name can't change.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanKeep), nameof(KeepLabel), nameof(KeepTip), nameof(CanEdit))]
    private bool _keeping;

    /// <summary>Why the save couldn't be kept, from the engine, said in the dialog.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    public bool HasError => Error is not null;

    /// <summary>The name the game's named saves have already, said as such; null when it's free.</summary>
    public string? Taken => Name.Trim() is { Length: > 0 } wanted && start.Names.Any(n => string.Equals(n.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
        ? $"{start.Title} has a named save called “{wanted}” already. Give this one another name, so you can tell them apart."
        : null;

    public bool HasTaken => Taken is not null;

    public bool CanKeep => !Keeping && Name.Trim().Length is > 0 and <= MaxName && Taken is null;

    public bool CanEdit => !Keeping;

    /// <summary>Busy, the button says Keeping with its dots counting up (KAN-80).</summary>
    public string KeepLabel => "Keep this save";

    /// <summary>Why Keep this save is off.</summary>
    public string? KeepTip => CanKeep || Keeping ? null : HasTaken ? "Give it another name" : "Give it a name first";

    [RelayCommand]
    private async Task Keep()
    {
        if (!CanKeep || actions?.KeepNamed is not { } keep)
        {
            return;
        }

        Error = null;
        Keeping = true;
        var failed = await keep(start.Id, Name.Trim());
        Keeping = false;
        if (failed is null)
        {
            actions.CloseDialog?.Invoke();
        }
        else
        {
            Error = failed;
        }
    }

    [RelayCommand]
    private void Cancel() => actions?.CloseDialog?.Invoke();

    [RelayCommand]
    private void Open()
    {
        if (start.Folder is { } folder)
        {
            actions?.OpenFolder?.Invoke(folder);
        }
    }
}
