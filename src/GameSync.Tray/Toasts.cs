using System.Security;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace GameSync.Tray;

/// <summary>Windows notifications (BG-05), for the things that need the person.</summary>
internal static class Toasts
{
    private const string AppId = "GameSync";

    public static void Show(string title, string message)
    {
        try
        {
            Register();
            var xml = new XmlDocument();
            xml.LoadXml($"<toast><visual><binding template=\"ToastGeneric\"><text>{SecurityElement.Escape(title)}</text><text>{SecurityElement.Escape(message)}</text></binding></visual></toast>");
            ToastNotificationManager.CreateToastNotifier(AppId).Show(new ToastNotification(xml));
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or IOException)
        {
            // Notifications can be off; the activity log still has it.
        }
    }

    /// <summary>A program outside the Store gives Windows its name for notifications under its own key in the user's registry.</summary>
    private static void Register()
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{AppId}");
        if (key.GetValue("DisplayName") as string != "GameSync")
        {
            key.SetValue("DisplayName", "GameSync");
        }
    }
}
