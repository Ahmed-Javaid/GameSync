using GameSync.Windows;

namespace GameSync.UI.Controls;

/// <summary>
/// Whether anything should move (A11Y-04): Windows' own "Animation effects" setting, read each time, so turning it off
/// takes effect at once. Fades, glows and the backdrop's light all ask here; with it off, they change at once.
/// </summary>
public static class Motion
{
    public static bool On
    {
        get
        {
            try
            {
                return WindowsLook.AnimationsOn();
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
            {
                // Not Windows, as in the snapshot tool on another system: things move.
                return true;
            }
        }
    }
}
