using System.Runtime.InteropServices;
using GameSync.Core.Safety;

namespace GameSync.Windows;

/// <summary>
/// R3: asks the installed antivirus about each restored file through AMSI, Windows' built-in "scan this" interface.
/// Microsoft Defender answers by default; any other antivirus that registers with AMSI does too.
/// </summary>
public sealed class AmsiScanner : IMalwareScanner, IDisposable
{
    // AMSI_RESULT_BLOCKED_BY_ADMIN_START: from here up, the answer is "don't use this content".
    private const int BlockedFrom = 0x4000;
    private const long MaxScanBytes = 256L * 1024 * 1024;

    private readonly IntPtr _context;

    public AmsiScanner()
    {
        if (AmsiInitialize("GameSync", out _context) != 0)
        {
            _context = IntPtr.Zero;
        }
    }

    public ScanVerdict ScanFile(string fullPath)
    {
        // Past the size limit, Defender's own real-time scan still checks the file as it's written.
        if (_context == IntPtr.Zero || new FileInfo(fullPath).Length > MaxScanBytes)
        {
            return ScanVerdict.Unavailable;
        }

        var bytes = File.ReadAllBytes(fullPath);
        if (AmsiOpenSession(_context, out var session) != 0)
        {
            return ScanVerdict.Unavailable;
        }

        try
        {
            if (AmsiScanBuffer(_context, bytes, (uint)bytes.Length, Path.GetFileName(fullPath), session, out var result) != 0)
            {
                return ScanVerdict.Unavailable;
            }

            return result >= BlockedFrom ? ScanVerdict.Detected : ScanVerdict.Clean;
        }
        finally
        {
            AmsiCloseSession(_context, session);
        }
    }

    public void Dispose()
    {
        if (_context != IntPtr.Zero)
        {
            AmsiUninitialize(_context);
        }
    }

    [DllImport("amsi.dll", CharSet = CharSet.Unicode)]
    private static extern int AmsiInitialize(string appName, out IntPtr amsiContext);

    [DllImport("amsi.dll")]
    private static extern void AmsiUninitialize(IntPtr amsiContext);

    [DllImport("amsi.dll")]
    private static extern int AmsiOpenSession(IntPtr amsiContext, out IntPtr amsiSession);

    [DllImport("amsi.dll")]
    private static extern void AmsiCloseSession(IntPtr amsiContext, IntPtr amsiSession);

    [DllImport("amsi.dll", CharSet = CharSet.Unicode)]
    private static extern int AmsiScanBuffer(IntPtr amsiContext, byte[] buffer, uint length, string contentName, IntPtr amsiSession, out int result);
}
