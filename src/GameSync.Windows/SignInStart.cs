using Microsoft.Win32;

namespace GameSync.Windows;

/// <summary>
/// BG-01: GameSync starts when the person signs in through their own Run key, with no admin rights, so Task Manager's
/// Startup apps lists it and can turn it off. Turned off there, it reads as off here too; turning it on here clears
/// Task Manager's switch.
/// </summary>
public sealed class SignInStart(RegistryKey root, string runPath = SignInStart.RunPath, string approvedPath = SignInStart.ApprovedPath)
{
    public const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    public const string Name = "GameSync";

    public static SignInStart ForThisUser() => new(Registry.CurrentUser);

    /// <summary>The command Windows runs at sign-in, when it's on.</summary>
    public string? Command
    {
        get
        {
            using var run = root.OpenSubKey(runPath);
            if (run?.GetValue(Name) is not string { Length: > 0 } command)
            {
                return null;
            }

            // Task Manager keeps its own switch: an odd first byte means turned off there.
            using var approved = root.OpenSubKey(approvedPath);
            return approved?.GetValue(Name) is byte[] { Length: > 0 } flags && (flags[0] & 1) == 1 ? null : command;
        }
    }

    public bool IsOn => Command is not null;

    public void TurnOn(string command)
    {
        using (var run = root.CreateSubKey(runPath))
        {
            run.SetValue(Name, command, RegistryValueKind.String);
        }

        ForgetTaskManagerSwitch();
    }

    public void TurnOff()
    {
        using (var run = root.OpenSubKey(runPath, writable: true))
        {
            run?.DeleteValue(Name, throwOnMissingValue: false);
        }

        ForgetTaskManagerSwitch();
    }

    private void ForgetTaskManagerSwitch()
    {
        using var approved = root.OpenSubKey(approvedPath, writable: true);
        approved?.DeleteValue(Name, throwOnMissingValue: false);
    }
}
