using GameSync.Core.Discovery;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Core.Storage;
using GameSync.Storage.Drive;

namespace GameSync.Host;

/// <summary>What one exchange with the cloud did: how many entries of other PCs' records it took in, and whether it wrote this PC's.</summary>
public sealed record AchievementExchange(int Pulled, bool Pushed);

/// <summary>
/// Design system version 54 (the owner, 8 Oct 2026: yes to keeping the record in the Drive folder): GameSync's record of
/// achievements kept in the cloud, so every PC shows the same. Each PC writes only its own file, <c>achievements/&lt;device&gt;.json</c>
/// in the GameSync folder beside the saves, and reads the others' when they change; entries settle the same whichever PC
/// meets them first (<see cref="KeptAchievements.Compare"/>), so the files never conflict. Settings, Achievements, Keep
/// them in the cloud turns it off; then the record stays on this PC only.
/// </summary>
public static class AchievementShare
{
    /// <summary><c>off</c> keeps the record on this PC only; on by default once a cloud is connected.</summary>
    public const string CloudKey = "achievements.cloud";

    private const string SeenPrefix = "achievements.cloud.seen.";
    private const string WrittenKey = "achievements.cloud.written";

    public static bool IsOn(StateStore state) => state.GetSetting(CloudKey) != "off";

    public static bool IsOn(string dataDir)
    {
        using var state = new StateStore(dataDir);
        return IsOn(state);
    }

    /// <summary>Keeps the record in the cloud, or on this PC only; turned on again, everything kept goes up at the next exchange.</summary>
    public static void SetOn(string dataDir, bool on)
    {
        using var state = new StateStore(dataDir);
        state.SetSetting(CloudKey, on ? "" : "off");
        if (on)
        {
            state.SetSetting(WrittenKey, "");
        }
    }

    /// <summary>One exchange with this PC's cloud; nothing with none connected, or with the record kept on this PC only.</summary>
    public static async Task<AchievementExchange> ExchangeAsync(string dataDir, CancellationToken ct)
    {
        var config = AppConfig.Load(dataDir);
        using var state = new StateStore(dataDir);
        if (config is null || config.HasNoCloud || !IsOn(state))
        {
            return new AchievementExchange(0, false);
        }

        using var drive = config.UsesDrive ? Cli.OpenDrive(dataDir) : null;
        ICloud cloud = drive is not null ? new DriveCloud(drive) : new FolderCloud(config.Remote);
        return await ExchangeAsync(cloud, state, state.GetOrCreateDevice(Environment.MachineName).Id, ct);
    }

    /// <summary>
    /// Takes in every other PC's file that changed since it was last read, then writes this PC's when its record changed
    /// since it was last written. This PC's own file is read back only when its record is empty, as after a reinstall.
    /// </summary>
    public static async Task<AchievementExchange> ExchangeAsync(ICloud cloud, StateStore state, DeviceId device, CancellationToken ct)
    {
        var pulled = 0;
        var empty = state.GetUnlocks().Count == 0;
        foreach (var file in await cloud.ListAchievementFilesAsync(ct))
        {
            var key = SeenPrefix + file.Device.Value;
            if ((file.Device == device && !empty) || state.GetSetting(key) == file.Tag)
            {
                continue;
            }

            if (await cloud.GetAchievementFileAsync(file.Device, KeptAchievements.LargestFile, ct) is { } bytes)
            {
                pulled += state.KeepUnlocks(KeptAchievements.Read(bytes, DateTime.UtcNow).Entries);
            }

            state.SetSetting(key, file.Tag);
        }

        // Read before the entries, so a change made while they go up goes up next time.
        var changed = state.GetSetting(StateStore.UnlocksChangedKey);
        if (changed is null || state.GetSetting(WrittenKey) == changed)
        {
            return new AchievementExchange(pulled, false);
        }

        await cloud.SaveAchievementFileAsync(device, KeptAchievements.Write(device, state.GetUnlocks(), DateTime.UtcNow), ct);
        state.SetSetting(WrittenKey, changed);
        return new AchievementExchange(pulled, true);
    }
}
