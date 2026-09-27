using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using GameSync.Core.Sessions;
using Microsoft.Win32.SafeHandles;

namespace GameSync.Windows;

/// <summary>
/// Finds the running processes of games with the least access (design.md → Sessions and launching): a system-wide
/// snapshot gives every process's name without opening any, and only a process named like one of a game's programs is
/// opened, once, with query-limited access (what Task Manager uses), for its path and start time. Nothing else is ever
/// done to a game's process.
/// </summary>
public sealed class ProcessWatcher
{
    private const uint SnapProcess = 0x00000002;
    private const uint QueryLimitedInformation = 0x1000;

    /// <summary>What each opened process turned out to be, so it's opened only once.</summary>
    private readonly Dictionary<int, (string Name, string? Path, DateTime StartedUtc)> _known = [];

    public IReadOnlyList<GameProcess> Find(IReadOnlyCollection<GamePrograms> games)
    {
        var byName = games.SelectMany(g => g.Names.Select(n => (Name: n, Game: g))).ToLookup(x => x.Name, x => x.Game, StringComparer.OrdinalIgnoreCase);
        var running = Snapshot();
        foreach (var gone in _known.Keys.Where(pid => !running.TryGetValue(pid, out var name) || !name.Equals(_known[pid].Name, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            _known.Remove(gone);
        }

        var found = new List<GameProcess>();
        foreach (var (pid, name) in running)
        {
            if (!byName.Contains(name))
            {
                continue;
            }

            if (!_known.TryGetValue(pid, out var info))
            {
                info = (name, null, DateTime.MinValue);
                if (Query(pid) is var (path, started))
                {
                    info = (name, path, started);
                }

                _known[pid] = info;
            }

            if (info.Path is { } processPath)
            {
                found.AddRange(byName[name].Where(g => g.Owns(processPath)).Select(g => new GameProcess(g.Game, pid, info.StartedUtc)));
            }
        }

        return found;
    }

    /// <summary>Every process's ID and program name, from one system-wide snapshot; no process is opened.</summary>
    private static Dictionary<int, string> Snapshot()
    {
        var processes = new Dictionary<int, string>();
        using var snapshot = CreateToolhelp32Snapshot(SnapProcess, 0);
        if (snapshot.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Couldn't list the running programs.");
        }

        var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
        for (var more = Process32First(snapshot, ref entry); more; more = Process32Next(snapshot, ref entry))
        {
            processes[(int)entry.ProcessId] = entry.ExeFile;
        }

        return processes;
    }

    /// <summary>The process's full path and start time, through a query-limited handle closed straight away; null when it's gone or not allowed.</summary>
    private static (string Path, DateTime StartedUtc)? Query(int pid)
    {
        using var handle = OpenProcess(QueryLimitedInformation, false, pid);
        if (handle.IsInvalid)
        {
            return null;
        }

        var path = new StringBuilder(1024);
        var size = path.Capacity;
        if (!QueryFullProcessImageName(handle, 0, path, ref size) || !GetProcessTimes(handle, out var created, out _, out _, out _))
        {
            return null;
        }

        return (path.ToString(0, size), DateTime.FromFileTimeUtc(created));
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32First(SafeFileHandle snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32Next(SafeFileHandle snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);
}
