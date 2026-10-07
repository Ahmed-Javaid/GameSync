using Avalonia.Media.Imaging;
using GameSync.UI.Theming;

namespace GameSync.Tray;

/// <summary>
/// Glossy's backdrops for the window (LOOK-17): made off the UI thread, one per art, strength and look, and the last few
/// kept, so moving between pages, opening the window again and switching between Dark and Light show them at once
/// (KAN-124). Each is a small picture (160 × 100).
/// </summary>
internal sealed class Backdrops(Action<Exception> failed)
{
    /// <summary>How many are kept: every strength of the art shown, in both modes, and a little more.</summary>
    private const int Kept = 12;

    private readonly Dictionary<Key, Bitmap?> _made = [];
    private readonly List<Key> _recent = [];
    private readonly Dictionary<Key, Task<Bitmap?>> _making = [];

    /// <summary>A backdrop is made, on the UI thread: what the window shows can be decided again.</summary>
    public event Action? Made;

    /// <summary>
    /// The backdrop once it's made, or null when the art can't be read and the page stays Solid; false while it's being
    /// made, and <see cref="Made"/> says when it is. Called on the UI thread.
    /// </summary>
    public bool TryGet(string art, GlassStrength strength, ThemeChoice choice, GlassSurface glass, IReadOnlyDictionary<string, string> theme, out Bitmap? backdrop)
    {
        var key = new Key(art, strength, choice);
        if (_made.TryGetValue(key, out backdrop))
        {
            Touch(key);
            return true;
        }

        _ = GetAsync(key, glass, theme);
        return false;
    }

    /// <summary>
    /// The backdrop, made now if it isn't kept yet: a change of look waits for its new backdrop, so the window changes once,
    /// all of it, instead of showing the new colours over the old glass first (KAN-124). Called on the UI thread.
    /// </summary>
    public Task<Bitmap?> GetAsync(string art, GlassStrength strength, ThemeChoice choice, GlassSurface glass, IReadOnlyDictionary<string, string> theme) =>
        GetAsync(new Key(art, strength, choice), glass, theme);

    private Task<Bitmap?> GetAsync(Key key, GlassSurface glass, IReadOnlyDictionary<string, string> theme)
    {
        if (_made.TryGetValue(key, out var made))
        {
            return Task.FromResult(made);
        }

        if (!_making.TryGetValue(key, out var making))
        {
            making = MakeAsync(key, glass, theme);
            _making[key] = making;
        }

        return making;
    }

    private async Task<Bitmap?> MakeAsync(Key key, GlassSurface glass, IReadOnlyDictionary<string, string> theme)
    {
        Bitmap? made = null;
        try
        {
            made = await Task.Run(() => Backdrop.Make(key.Art, glass, theme));
        }
        catch (Exception e)
        {
            failed(e);
        }

        _making.Remove(key);
        _made[key] = made;
        Touch(key);

        // The least recently shown go; a picture still on screen is held by the window until it's replaced.
        while (_recent.Count > Kept)
        {
            _made.Remove(_recent[0]);
            _recent.RemoveAt(0);
        }

        Made?.Invoke();
        return made;
    }

    private void Touch(Key key)
    {
        _recent.Remove(key);
        _recent.Add(key);
    }

    private sealed record Key(string Art, GlassStrength Strength, ThemeChoice Choice);
}
