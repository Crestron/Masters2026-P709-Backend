using Microsoft.AspNetCore.SignalR;
using P709.Core.Configuration;
using P709.Core.Nvx;
using P709.Web.Hubs;

namespace P709.Web.Mcp;

/// <summary>
/// The room as the MCP tools see it: every value an AI can read and every command it can run, worked out from the room
/// configuration and <see cref="RoomState"/>. Commands change the same state and send the same events, with the same names
/// and arguments, as <see cref="RoomHub"/>, so whatever the AI does shows on every panel.
/// </summary>
/// <remarks>
/// Plain C#, with no MCP or Crestron types, so it is tested without a processor. The Crestron project registers one MCP tool
/// per member (<c>McpRegistration</c>). Items are indexed from 0, in room-configuration order, as MCP indexes them.
/// <see cref="RoomHub"/> is left exactly as the earlier labs built it, so the few lines of hub logic needed here are repeated
/// rather than shared.
/// </remarks>
public sealed class RoomTools(
    RoomConfiguration roomConfiguration,
    RoomState roomState,
    NvxRouter nvxRouter,
    IHubContext<RoomHub> hubContext,
    ILogger<RoomTools> logger)
{
    /// <summary>Names of the lights, in configuration order: the item names of the light tools.</summary>
    public IReadOnlyList<string> LightNames { get; } = roomConfiguration.Lights.Select(light => light.Name).ToList();

    /// <summary>Names of the lighting presets, in configuration order: the item names of the preset tools.</summary>
    public IReadOnlyList<string> PresetNames { get; } = roomConfiguration.LightingPresets.Select(preset => preset.Name).ToList();

    private IClientProxy Panels => hubContext.Clients.All;

    /// <summary>Level, 0 to 100, of the light at <paramref name="index"/>. A light never set reports 0.</summary>
    public ushort GetLightLevel(int index) => (ushort)roomState.LightLevels.GetValueOrDefault(LightAt(index).Id);

    /// <summary>Whether the preset at <paramref name="index"/> is the current scene.</summary>
    public bool IsPresetSelected(int index) => roomState.SelectedPresetId == PresetAt(index).Id;

    /// <summary>Volume, 0 to 100.</summary>
    public ushort GetVolumeLevel() => (ushort)roomState.VolumeLevelPercent;

    /// <summary>Whether audio is muted.</summary>
    public bool IsAudioMuted() => roomState.IsAudioMuted;

    /// <summary>
    /// Sets the light at <paramref name="index"/> to <paramref name="levelPercent"/>, capped at 100. A switch light goes fully on
    /// from 50 and fully off below it. Like <see cref="RoomHub.SetLightLevel"/>, this ends the current scene in the room state
    /// and sends only <c>LightLevelChanged</c>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">There is no light at <paramref name="index"/>.</exception>
    public async Task SetLightLevelAsync(int index, ushort levelPercent)
    {
        var light = LightAt(index);
        int level = Math.Min(levelPercent, (ushort)100);

        if (light.Type == LightType.Switch)
        {
            level = level >= 50 ? 100 : 0;
        }

        // TODO: Lab 4.4 - Set the light level for the AI, and tell every panel
        // Hint: This is what RoomHub.SetLightLevel does, but from outside the hub: there is no Clients property here, so send
        //       through the hub context (Panels is a shortcut for hubContext.Clients.All). Update the room state, then send the
        //       same LightLevelChanged event with the same arguments, so the panel needs no new code.
        // Ref:  https://learn.microsoft.com/en-us/aspnet/core/signalr/hubcontext?view=aspnetcore-10.0
        // Add your work below this line

        // Lab 4.4 - End
    }

    /// <summary>Recalls the preset at <paramref name="index"/>, as <see cref="RoomHub.SelectPreset"/> does.</summary>
    /// <exception cref="ArgumentOutOfRangeException">There is no preset at <paramref name="index"/>.</exception>
    public async Task SelectPresetAsync(int index)
    {
        var preset = PresetAt(index);

        foreach (var level in preset.Levels)
        {
            roomState.SetLightLevel(level.LightId, level.LevelPercent);
            await Panels.SendAsync("LightLevelChanged", level.LightId, level.LevelPercent);
        }

        roomState.SetSelectedPreset(preset.Id);
        await Panels.SendAsync("PresetSelected", preset.Id);
    }

    /// <summary>Sets the volume, capped at 100, and tells every panel.</summary>
    public async Task SetVolumeLevelAsync(ushort levelPercent)
    {
        int level = Math.Min(levelPercent, (ushort)100);

        roomState.SetVolumeLevel(level);
        await Panels.SendAsync("VolumeLevelChanged", level);
    }

    /// <summary>Mutes or unmutes audio, and tells every panel.</summary>
    public async Task SetAudioMutedAsync(bool isMuted)
    {
        roomState.SetAudioMuted(isMuted);
        await Panels.SendAsync("AudioMutedChanged", isMuted);
    }

    /// <summary>The source index MCP reads when no source has been selected.</summary>
    public const ushort NoSource = ushort.MaxValue;

    /// <summary>
    /// How long the system takes to go from Starting to On, or Stopping to Off. The same ten seconds as <see cref="RoomHub"/>;
    /// tests shorten it.
    /// </summary>
    public TimeSpan PowerSequenceDuration { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Names of the sources, in configuration order: what the AI picks from when routing.</summary>
    public IReadOnlyList<string> SourceNames { get; } = roomConfiguration.Sources.Select(source => source.Name).ToList();

    /// <summary>The MCP source type code of each source, in configuration order.</summary>
    public IReadOnlyList<ushort> SourceTypes { get; } = roomConfiguration.Sources.Select(source => ToMcpSourceType(source.Type)).ToList();

    /// <summary>Whether the system is fully on. Starting and Stopping read as off.</summary>
    public bool IsSystemOn() => roomState.SystemPower == SystemPowerState.On;

    /// <summary>Index of the selected source, or <see cref="NoSource"/> when none has been selected.</summary>
    public ushort GetCurrentSourceIndex()
    {
        var index = roomConfiguration.Sources.ToList().FindIndex(source => source.Id == roomState.SelectedSourceId);

        return index < 0 ? NoSource : (ushort)index;
    }

    /// <summary>
    /// The MCP Gateway's numeric source type for a P709 <see cref="SourceType"/>. The toolkit publishes no table of these, so
    /// the values follow the McpStarter example program.
    /// </summary>
    public static ushort ToMcpSourceType(SourceType type) => type switch
    {
        SourceType.WirelessPresenter => 410,
        SourceType.LaptopHdmi or SourceType.LaptopUsbC => 10,
        SourceType.Tv => 230,
        SourceType.Other => 20,
        _ => 0
    };

    /// <summary>
    /// Turns the system on or off with the same sequence as <see cref="RoomHub.SetSystemPower"/>: on only from Off, off only
    /// from On, so a request in the middle of a sequence does nothing.
    /// </summary>
    public async Task SetSystemPowerAsync(bool isOn)
    {
        if (isOn && roomState.SystemPower == SystemPowerState.Off)
        {
            await RunPowerSequenceAsync(SystemPowerState.Starting, SystemPowerState.On);
        }
        else if (!isOn && roomState.SystemPower == SystemPowerState.On)
        {
            await RunPowerSequenceAsync(SystemPowerState.Stopping, SystemPowerState.Off);
        }
    }

    /// <summary>
    /// Selects the source at <paramref name="index"/>, as <see cref="RoomHub.SelectSource"/> does: starts the system first if it
    /// is off, and routes a source that arrives over NVX before changing anything. As with the hub, the source is selected even
    /// if the NVX fails (it may be offline); the stream is then reported as not running.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">There is no source at <paramref name="index"/>.</exception>
    public async Task SelectSourceAsync(int index)
    {
        var sourceId = ItemAt(roomConfiguration.Sources, index, "source").Id;

        if (roomState.SystemPower == SystemPowerState.Off)
        {
            await RunPowerSequenceAsync(SystemPowerState.Starting, SystemPowerState.On);
        }

        var isNvxSource = nvxRouter.HasRoute(sourceId);
        var isRouted = false;

        if (isNvxSource)
        {
            try
            {
                isRouted = await nvxRouter.RouteSourceAsync(sourceId);
            }
            catch (NvxUnreachableException exception)
            {
                // Going offline is logged once by the web host; this would repeat on every selection.
                logger.LogDebug("Could not route source \"{SourceId}\": {Reason}", sourceId, exception.Message);
            }
            catch (NvxException exception)
            {
                logger.LogError(exception, "Could not route source \"{SourceId}\": {Reason}", sourceId, exception.Message);
            }
        }

        roomState.SelectSource(sourceId);
        await Panels.SendAsync("SelectedSourceChanged", sourceId);

        if (isNvxSource)
        {
            roomState.SetStreamRunning(isRouted);
            await Panels.SendAsync("StreamRunningChanged", isRouted);
        }
    }

    // Runs Off -> Starting -> On or On -> Stopping -> Off, broadcasting each step, as RoomHub does.
    private async Task RunPowerSequenceAsync(SystemPowerState during, SystemPowerState after)
    {
        roomState.SetSystemPower(during);
        await Panels.SendAsync("SystemPowerStateChanged", during);

        await Task.Delay(PowerSequenceDuration);

        roomState.SetSystemPower(after);
        await Panels.SendAsync("SystemPowerStateChanged", after);
    }

    private LightConfig LightAt(int index) => ItemAt(roomConfiguration.Lights, index, "light");

    private LightingPresetConfig PresetAt(int index) => ItemAt(roomConfiguration.LightingPresets, index, "lighting preset");

    private static T ItemAt<T>(IReadOnlyList<T> items, int index, string kind) =>
        index >= 0 && index < items.Count
            ? items[index]
            : throw new ArgumentOutOfRangeException(nameof(index), index, $"There is no {kind} {index}: the room has {items.Count} (indexed from 0).");
}
