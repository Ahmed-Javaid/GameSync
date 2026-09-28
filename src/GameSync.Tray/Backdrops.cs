using Avalonia.Media.Imaging;
using GameSync.UI.Theming;

namespace GameSync.Tray;

/// <summary>
/// Glossy's backdrops for the window (LOOK-17): made off the UI thread, one per strength for the art and theme shown, and
/// kept, so moving between pages and opening the window again show them at once. That's three small pictures at most.
/// </summary>
internal sealed class Backdrops(Action<Exception> failed)
{
    private readonly Dictionary<Key, Bitmap?> _made = [];
    private readonly HashSet<Key> _making = [];

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
            return true;
        }

        if (_making.Add(key))
        {
            _ = MakeAsync(key, glass, theme);
        }

        return false;
    }

    private async Task MakeAsync(Key key, GlassSurface glass, IReadOnlyDictionary<string, string> theme)
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
        foreach (var old in _made.Keys.Where(k => k.Art != key.Art || k.Choice != key.Choice).ToList())
        {
            _made.Remove(old);
        }

        _made[key] = made;
        Made?.Invoke();
    }

    private sealed record Key(string Art, GlassStrength Strength, ThemeChoice Choice);
}
