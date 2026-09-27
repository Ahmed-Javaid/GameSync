using System.Buffers.Binary;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using GameSync.Core.Scanning;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace GameSync.Windows;

/// <summary>
/// FIND-10 on Windows: keys under HKEY_CURRENT_USER\Software only, read for backups and written only by restores that
/// passed R7. Values are read and written as their type and bytes, so every one round-trips exactly, including odd
/// ones like Unity's 8-byte REG_DWORD floats. A write checks the whole export first, so a bad one changes nothing.
/// </summary>
public sealed class WindowsRegistry : IRegistryStore
{
    private const int String = 1;
    private const int ExpandString = 2;
    private const int Binary = 3;
    private const int DWord = 4;
    private const int MultiString = 7;
    private const int QWord = 11;
    private const int MoreData = 234;

    public (RegistryNode Node, DateTime ChangedUtc)? Export(string key)
    {
        if (SubKey(key) is not { } path)
        {
            return null;
        }

        using var handle = Registry.CurrentUser.OpenSubKey(path, writable: false);
        if (handle is null)
        {
            return null;
        }

        var changed = DateTime.MinValue;
        var node = Read(handle, ref changed);
        return (node, changed == DateTime.MinValue ? DateTime.UtcNow : changed);
    }

    public void Import(string key, RegistryNode node)
    {
        var path = SubKey(key) ?? throw new InvalidOperationException($"{key} isn't under HKEY_CURRENT_USER\\Software.");
        Check(node);
        using var handle = Registry.CurrentUser.CreateSubKey(path, writable: true);
        Write(handle, node);
    }

    /// <summary>"HKEY_CURRENT_USER/Software/X" or "HKCU\Software\X" as "Software\X"; null for anything else.</summary>
    private static string? SubKey(string key)
    {
        var parts = key.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 && parts[0].ToUpperInvariant() is "HKCU" or "HKEY_CURRENT_USER" && parts[1].Equals("Software", StringComparison.OrdinalIgnoreCase)
            ? string.Join('\\', parts[1..])
            : null;
    }

    private static RegistryNode Read(RegistryKey key, ref DateTime changed)
    {
        if (LastWrite(key) is { } last && last > changed)
        {
            changed = last;
        }

        var node = new RegistryNode();
        foreach (var name in key.GetValueNames())
        {
            if (Query(key, name) is var (type, bytes))
            {
                node.Values[name] = ToValue(type, bytes);
            }
        }

        foreach (var name in key.GetSubKeyNames())
        {
            using var child = key.OpenSubKey(name, writable: false);
            if (child is not null)
            {
                node.Keys[name] = Read(child, ref changed);
            }
        }

        return node;
    }

    /// <summary>A readable value where the bytes are the usual shape for their type; otherwise the type and bytes as they are.</summary>
    private static RegistryValue ToValue(int type, byte[] bytes) => type switch
    {
        String or ExpandString when bytes.Length % 2 == 0 => new RegistryValue(type == String ? "String" : "ExpandString", Text(bytes)),
        MultiString when bytes.Length % 2 == 0 => new RegistryValue("MultiString", Lines: Lines(Text(bytes))),
        DWord when bytes.Length == 4 => new RegistryValue("DWord", BinaryPrimitives.ReadUInt32LittleEndian(bytes).ToString(CultureInfo.InvariantCulture)),
        QWord when bytes.Length == 8 => new RegistryValue("QWord", BinaryPrimitives.ReadUInt64LittleEndian(bytes).ToString(CultureInfo.InvariantCulture)),
        Binary => new RegistryValue("Binary", Convert.ToBase64String(bytes)),
        _ => new RegistryValue("Raw", Convert.ToBase64String(bytes), Type: type),
    };

    /// <summary>UTF-16 text without its closing null.</summary>
    private static string Text(byte[] bytes)
    {
        var text = Encoding.Unicode.GetString(bytes);
        return text.EndsWith('\0') ? text[..^1] : text;
    }

    /// <summary>A REG_MULTI_SZ's strings: each ends with a null, and the list with one more.</summary>
    private static List<string> Lines(string text)
    {
        var lines = text.Split('\0').ToList();
        if (lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }

    private static (int Type, byte[] Bytes) ToBytes(RegistryValue value)
    {
        try
        {
            return value.Kind switch
            {
                "String" => (String, Encoding.Unicode.GetBytes((value.Data ?? "") + '\0')),
                "ExpandString" => (ExpandString, Encoding.Unicode.GetBytes((value.Data ?? "") + '\0')),
                "MultiString" => (MultiString, Encoding.Unicode.GetBytes(string.Concat((value.Lines ?? []).Select(l => l + '\0')) + '\0')),
                "DWord" => (DWord, LittleEndian(uint.Parse(value.Data!, CultureInfo.InvariantCulture))),
                "QWord" => (QWord, LittleEndian(ulong.Parse(value.Data!, CultureInfo.InvariantCulture))),
                "Binary" => (Binary, Convert.FromBase64String(value.Data ?? "")),
                // Never REG_LINK (6), which redirects a key, nor the hardware resource types (8 to 10).
                "Raw" when value.Type is (>= 0 and <= 5) or 7 or 11 => (value.Type.Value, Convert.FromBase64String(value.Data ?? "")),
                _ => throw new FormatException($"'{value.Kind}' isn't a kind of registry value GameSync writes."),
            };
        }
        catch (Exception e) when (e is ArgumentNullException or OverflowException)
        {
            throw new FormatException($"A {value.Kind} value in a registry export can't be read.", e);
        }
    }

    private static byte[] LittleEndian(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return bytes;
    }

    private static byte[] LittleEndian(ulong value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        return bytes;
    }

    private static void Check(RegistryNode node)
    {
        foreach (var value in node.Values.Values)
        {
            _ = ToBytes(value);
        }

        foreach (var child in node.Keys.Values)
        {
            Check(child);
        }
    }

    private static void Write(RegistryKey key, RegistryNode node)
    {
        foreach (var name in key.GetValueNames().Where(n => !node.Values.ContainsKey(n)))
        {
            key.DeleteValue(name, throwOnMissingValue: false);
        }

        foreach (var (name, value) in node.Values)
        {
            var (type, bytes) = ToBytes(value);
            var result = RegSetValueEx(key.Handle, name, 0, type, bytes, bytes.Length);
            if (result != 0)
            {
                throw new IOException($"Couldn't write the registry value '{name}': {new Win32Exception(result).Message}");
            }
        }

        foreach (var name in key.GetSubKeyNames().Where(n => !node.Keys.ContainsKey(n)))
        {
            key.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
        }

        foreach (var (name, child) in node.Keys)
        {
            using var sub = key.CreateSubKey(name, writable: true);
            Write(sub, child);
        }
    }

    private static (int Type, byte[] Bytes)? Query(RegistryKey key, string name)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var size = 0;
            var result = RegQueryValueEx(key.Handle, name, IntPtr.Zero, out var type, null, ref size);
            if (result != 0)
            {
                return null;
            }

            var bytes = new byte[size];
            result = RegQueryValueEx(key.Handle, name, IntPtr.Zero, out type, bytes, ref size);
            if (result == 0)
            {
                return (type, bytes[..size]);
            }

            if (result != MoreData)
            {
                return null;
            }
        }

        return null;
    }

    private static DateTime? LastWrite(RegistryKey key) =>
        RegQueryInfoKey(key.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
            IntPtr.Zero, out var fileTime) == 0
            ? DateTime.FromFileTimeUtc(fileTime)
            : null;

    [DllImport("advapi32.dll", EntryPoint = "RegQueryInfoKeyW", CharSet = CharSet.Unicode)]
    private static extern int RegQueryInfoKey(SafeRegistryHandle key, IntPtr className, IntPtr classLength, IntPtr reserved, IntPtr subKeys,
        IntPtr maxSubKeyLength, IntPtr maxClassLength, IntPtr values, IntPtr maxValueNameLength, IntPtr maxValueLength, IntPtr securityDescriptor,
        out long lastWriteTime);

    [DllImport("advapi32.dll", EntryPoint = "RegQueryValueExW", CharSet = CharSet.Unicode)]
    private static extern int RegQueryValueEx(SafeRegistryHandle key, string name, IntPtr reserved, out int type, byte[]? data, ref int size);

    [DllImport("advapi32.dll", EntryPoint = "RegSetValueExW", CharSet = CharSet.Unicode)]
    private static extern int RegSetValueEx(SafeRegistryHandle key, string name, int reserved, int type, byte[] data, int size);
}
