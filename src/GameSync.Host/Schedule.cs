using System.Diagnostics;
using System.Security;
using System.Text;
using System.Xml;

namespace GameSync.Host;

/// <summary>
/// The Windows Task Scheduler tasks GameSync keeps for this user (BG-01, BG-02, SET-02): the daily backup, a catch-up
/// about 10 minutes after sign-in when the PC was off at the daily time, and the background app at sign-in. They run as
/// this user, without admin rights, at below-normal priority, and never wake the PC.
/// </summary>
internal static class Schedule
{
    public const string DailyTask = "GameSync daily backup";
    public const string CatchUpTask = "GameSync catch-up";
    public const string BackgroundTask = "GameSync background";
    public static readonly TimeSpan CatchUpDelay = TimeSpan.FromMinutes(10);

    /// <summary>The background app, next to this program.</summary>
    public static string BackgroundProgram => Path.Combine(AppContext.BaseDirectory, "GameSync.Tray.exe");

    public static string DailyTrigger(TimeOnly at, DateTime todayLocal) =>
        $"<CalendarTrigger><StartBoundary>{(todayLocal.Date + at.ToTimeSpan()).ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)}</StartBoundary>" +
        "<Enabled>true</Enabled><ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay></CalendarTrigger>";

    public static string SignInTrigger(TimeSpan? delay) =>
        $"<LogonTrigger><Enabled>true</Enabled><UserId>{Escape(User)}</UserId>{(delay is { } d ? $"<Delay>{XmlConvert.ToString(d)}</Delay>" : "")}</LogonTrigger>";

    /// <summary>A task for this user as Task Scheduler's XML.</summary>
    /// <param name="unlimited">Runs as long as it likes, like the background app; otherwise it's stopped after two hours.</param>
    public static string TaskXml(string description, string trigger, string program, string arguments, bool unlimited) => $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo><Description>{Escape(description)}</Description></RegistrationInfo>
          <Triggers>{trigger}</Triggers>
          <Principals><Principal id="Author"><UserId>{Escape(User)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
          <Settings>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <StartWhenAvailable>false</StartWhenAvailable>
            <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
            <WakeToRun>false</WakeToRun>
            <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
            <AllowStartOnDemand>true</AllowStartOnDemand>
            <Enabled>true</Enabled>
            <Hidden>false</Hidden>
            <RunOnlyIfIdle>false</RunOnlyIfIdle>
            <ExecutionTimeLimit>{(unlimited ? "PT0S" : "PT2H")}</ExecutionTimeLimit>
            <Priority>7</Priority>
          </Settings>
          <Actions Context="Author"><Exec><Command>{Escape(program)}</Command><Arguments>{Escape(arguments)}</Arguments></Exec></Actions>
        </Task>
        """;

    /// <summary>Adds or replaces a task; Task Scheduler reads the XML from a file.</summary>
    public static void Register(string name, string xml)
    {
        var file = Path.Combine(Path.GetTempPath(), $"gamesync-task-{Guid.NewGuid():N}.xml");
        File.WriteAllText(file, xml, Encoding.Unicode);
        try
        {
            var (code, output) = Run("/Create", "/TN", name, "/XML", file, "/F");
            if (code != 0)
            {
                throw new InvalidOperationException($"Windows didn't add the task '{name}': {output.Trim()}");
            }
        }
        finally
        {
            File.Delete(file);
        }
    }

    public static void Remove(string name)
    {
        if (Exists(name))
        {
            Run("/Delete", "/TN", name, "/F");
        }
    }

    public static bool Exists(string name) => Run("/Query", "/TN", name).Code == 0;

    /// <summary>The program's arguments: the command, and the data folder when it isn't the usual one.</summary>
    public static string Arguments(string command, string dataDir) =>
        Path.GetFullPath(dataDir).Equals(Path.GetFullPath(Engine.DefaultDataDir), StringComparison.OrdinalIgnoreCase) ? command : $"--data \"{dataDir}\" {command}";

    private static string User => $"{Environment.UserDomainName}\\{Environment.UserName}";

    private static string Escape(string text) => SecurityElement.Escape(text);

    private static (int Code, string Output) Run(params string[] arguments)
    {
        var start = new ProcessStartInfo("schtasks.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Task Scheduler didn't start.");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }
}
