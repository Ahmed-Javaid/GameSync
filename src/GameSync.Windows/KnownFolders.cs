using System.Runtime.InteropServices;

namespace GameSync.Windows;

/// <summary>This PC's folders for the portable placeholders (FIND-08), such as <c>&lt;documents&gt;</c>, redirection included.</summary>
public static class KnownFolders
{
    private static readonly Guid LocalAppDataLow = new("A520A1A4-1780-4FF6-BD18-167343C5AF16");
    private static readonly Guid SavedGames = new("4C5C32FF-BB9D-43B0-B5B4-2D72E54EAAA4");
    private static readonly Guid DownloadsFolder = new("374DE290-123F-4565-9164-39C4925E467B");

    /// <summary>The person's Downloads folder, where shared zips go unless they choose another (FOLD-09); null when Windows doesn't say.</summary>
    public static string? Downloads() => Get(DownloadsFolder);

    public static IReadOnlyDictionary<string, string> ForThisPc()
    {
        var folders = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["<home>"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ["<documents>"] = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            ["<public>"] = Environment.GetEnvironmentVariable("PUBLIC"),
            ["<publicDocuments>"] = Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),
            ["<roaming>"] = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ["<localAppData>"] = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ["<programData>"] = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            ["<localLow>"] = Get(LocalAppDataLow),
            ["<savedGames>"] = Get(SavedGames),
        };

        return folders
            .Where(f => !string.IsNullOrEmpty(f.Value))
            .ToDictionary(f => f.Key, f => f.Value!, StringComparer.OrdinalIgnoreCase);
    }

    private static string? Get(Guid id)
    {
        var result = SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var path);
        try
        {
            return result == 0 ? Marshal.PtrToStringUni(path) : null;
        }
        finally
        {
            Marshal.FreeCoTaskMem(path);
        }
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint flags, IntPtr token, out IntPtr path);
}
