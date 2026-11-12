using Microsoft.AspNetCore.SignalR;
using P709.Core.Configuration;
using P709.Core.Connection;
using P709.Core.Nvx;
using P709.Web.Nvx;

namespace P709.Web.Hubs;

/// <summary>
/// Hub the front-end connects to. Exposes the configuration query plus commands and their corresponding change events,
/// per the Unified Room Control API.
/// </summary>
public class RoomHub(RoomConfiguration roomConfiguration, RoomState roomState, NvxRouter nvxRouter, ILogger<RoomHub> logger) : Hub
{
    /// <summary>URL path the hub is mapped to: the same path published to the panel in the connection details.</summary>
    public const string Path = ConnectionDetails.HubPath;

    /// <summary>How long the system takes to go from <see cref="SystemPowerState.Starting"/> to <see cref="SystemPowerState.On"/>.</summary>
    private static readonly TimeSpan PowerSequenceDuration = TimeSpan.FromSeconds(10);

    // TODO: Lab 1.2a - Query: return the room configuration to the client
    // Hint: Add a public method to the SignalR Hub to handle a request to returns the roomConfiguration, a singleton that has been passed
    //       to the constructor via dependency injection.
    // Ref: The README.md holds definition of the SignalR API
    // Add your work below this line

    // Lab 1.2a - End

    // REF: Lab 1.2a - Query: return the room configuration to the client
    /// <summary>Query: the front-end calls this after connecting to learn how to build its UI.</summary>
    public RoomConfiguration GetRoomConfiguration() => roomConfiguration;
    // REF: Lab 1.2a - End

    // TODO: Lab 1.3 - Hook up the lighting SignalR commands
    // Hint: Add public methods for each of the commands for setting the light level and selecting a preset.
    //       Each method should update the roomState and broadcast the corresponding change event to all clients.
    //       SelectPreset should also record the preset with roomState.SetSelectedPreset, after setting the levels.
    // Add your work below this line

    // Lab 1.3 - End

    // REF: Lab 1.3 - Hook up the lighting SignalR commands
    /// <summary>Lighting command: sets a light's level.</summary>
    /// <remarks>Note! The usual C# Async naming convention is not used here.</remarks>
    public async Task SetLightLevel(string lightId, int levelPercent)
    {
        roomState.SetLightLevel(lightId, levelPercent);
        await Clients.All.SendAsync("LightLevelChanged", lightId, levelPercent);
    }

    /// <summary>Lighting command: recalls a preset, applying and broadcasting each light level it sets, then broadcasting PresetSelected.</summary>
    public async Task SelectPreset(SelectPresetRequest request)
    {
        if (request is null)
        {
            return;
        }

        var preset = roomConfiguration.LightingPresets.FirstOrDefault(p => p.Id == request.PresetId);

        if (preset is null)
        {
            throw new HubException($"Unknown preset id \"{request.PresetId}\".");
        }

        foreach (var level in preset.Levels)
        {
            roomState.SetLightLevel(level.LightId, level.LevelPercent);

            await Clients.All.SendAsync("LightLevelChanged", level.LightId, level.LevelPercent);
        }

        roomState.SetSelectedPreset(request.PresetId);
        await Clients.All.SendAsync("PresetSelected", request.PresetId);
    }
    // REF: Lab 1.3 - End

    /// <summary>
    /// Query: a snapshot of the whole room state, so a client that has just connected (or reconnected) can show the room
    /// as it is now rather than waiting for the next change.
    /// </summary>
    /// <returns>
    /// The current state. <see cref="RoomStateSnapshot.LightLevels"/> and <see cref="RoomStateSnapshot.ShadePositions"/> list
    /// every configured light and shade in configuration order; one that has not been set yet reports 0.
    /// </returns>
    public RoomStateSnapshot GetRoomState()
    {
        var lightLevels = roomConfiguration.Lights
            .Select(light => new LightLevel(light.Id, roomState.LightLevels.GetValueOrDefault(light.Id)))
            .ToList();

        var shadePositions = roomConfiguration.Shades
            .Select(shade => new ShadePosition(shade.Id, roomState.ShadePositions.GetValueOrDefault(shade.Id)))
            .ToList();

        var shadeTilts = roomConfiguration.Shades
            .Where(shade => shade.SupportsTilt)
            .Select(shade => new ShadeTilt(shade.Id, roomState.ShadeTilts.GetValueOrDefault(shade.Id)))
            .ToList();

        return new RoomStateSnapshot(
            roomState.SystemPower,
            roomState.VolumeLevelPercent,
            roomState.IsAudioMuted,
            roomState.SelectedSourceId,
            roomState.CurrentPage,
            roomState.SelectedPresetId,
            lightLevels,
            shadePositions,
            shadeTilts,
            roomState.SelectedShadePresetId,
            roomState.IsStreamRunning);
    }

    /// <summary>Shades command: moves a shade to a position, 0 (closed) to 100 (open), and broadcasts ShadePositionChanged to every client.</summary>
    public async Task SetShadePosition(string shadeId, int positionPercent)
    {
        if (roomConfiguration.Shades.All(shade => shade.Id != shadeId))
        {
            throw new HubException($"Unknown shade id \"{shadeId}\".");
        }

        if (positionPercent is < 0 or > 100)
        {
            throw new HubException($"Shade position must be between 0 and 100, was {positionPercent}.");
        }

        var hadActivePreset = roomState.SelectedShadePresetId is not null;
        roomState.SetShadePosition(shadeId, positionPercent);
        await Clients.All.SendAsync("ShadePositionChanged", shadeId, positionPercent);

        if (hadActivePreset)
        {
            await Clients.All.SendAsync("ShadePresetSelected", (string?)null);
        }
    }

    /// <summary>Shades command: tilts a shade's slats, 0 to 100, and broadcasts ShadeTiltChanged to every client.</summary>
    public async Task SetShadeTilt(string shadeId, int tiltPercent)
    {
        var shade = roomConfiguration.Shades.FirstOrDefault(shade => shade.Id == shadeId);

        if (shade is null)
        {
            throw new HubException($"Unknown shade id \"{shadeId}\".");
        }

        if (!shade.SupportsTilt)
        {
            throw new HubException($"Shade \"{shadeId}\" does not support tilt.");
        }

        if (tiltPercent is < 0 or > 100)
        {
            throw new HubException($"Shade tilt must be between 0 and 100, was {tiltPercent}.");
        }

        var hadActivePreset = roomState.SelectedShadePresetId is not null;
        roomState.SetShadeTilt(shadeId, tiltPercent);
        await Clients.All.SendAsync("ShadeTiltChanged", shadeId, tiltPercent);

        if (hadActivePreset)
        {
            await Clients.All.SendAsync("ShadePresetSelected", (string?)null);
        }
    }

    /// <summary>Shades command: recalls a shade preset, applying and broadcasting each position (and tilt) it sets, then broadcasting ShadePresetSelected.</summary>
    public async Task SelectShadePreset(SelectShadePresetRequest request)
    {
        if (request is null)
        {
            return;
        }

        var preset = (roomConfiguration.ShadePresets ?? []).FirstOrDefault(p => p.Id == request.PresetId);

        if (preset is null)
        {
            throw new HubException($"Unknown shade preset id \"{request.PresetId}\".");
        }

        foreach (var target in preset.Targets)
        {
            roomState.SetShadePosition(target.ShadeId, target.PositionPercent);
            await Clients.All.SendAsync("ShadePositionChanged", target.ShadeId, target.PositionPercent);

            if (target.TiltPercent is int tiltPercent)
            {
                roomState.SetShadeTilt(target.ShadeId, tiltPercent);
                await Clients.All.SendAsync("ShadeTiltChanged", target.ShadeId, tiltPercent);
            }
        }

        roomState.SetSelectedShadePreset(request.PresetId);
        await Clients.All.SendAsync("ShadePresetSelected", request.PresetId);
    }

    /// <summary>Power command: turns the system on or off.</summary>
    public async Task SetSystemPower(bool isOn)
    {
        if (isOn)
        {
            if (roomState.SystemPower == SystemPowerState.Off)
            {
                await StartSystemAsync();
            }
        }
        else
        {
            if (roomState.SystemPower == SystemPowerState.On)
            {
                await StopSystemAsync();
            }
        }
    }

    // TODO: Lab 1.4 - Command: set the volume level in the room state
    // Hint: Add a public method to set the room's volume level, which updates the roomState with the new value.
    //       Broadcast the change to all clients.
    // Ref:  The README.md holds definition of the SignalR API; other methods follow a similar pattern
    // Add your work below this line

    // Lab 1.4 - End

    // REF: Lab 1.4 - Command: set the volume level in the room state
    /// <summary>Volume command: sets the volume level.</summary>
    public async Task SetVolumeLevel(int levelPercent)
    {
        roomState.SetVolumeLevel(levelPercent);
        await Clients.All.SendAsync("VolumeLevelChanged", levelPercent);
    }
    // REF: Lab 1.4 - End

    // TODO: Lab 1.2b - Implement the Hub.OnConnectedAsync override
    // Hint: Implement an override for the Hub<> connection event handler
    //       call the base implementation to maintain the standard behaviour
    //       then send the current SystemPowerState (found in roomState) to the caller.
    // Ref:  Refer to the SignalR API spec in the README.md, and other RoomHub methods that communicate feedback to the UI
    // Add your work below this line

    // Lab 1.2b - End

    // REF: Lab 1.2b - Implement the Hub.OnConnectedAsync override
    /// <inheritdoc />
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
        await Clients.Caller.SendAsync("SystemPowerStateChanged", roomState.SystemPower);
    }
    // REF: Lab 1.2b - End

    /// <summary>Page synchronization command: tells other clients which page is current.</summary>
    public async Task SetCurrentPage(PageKey pageKey)
    {
        roomState.SetCurrentPage(pageKey);
        await Clients.AllExcept(Context.ConnectionId).SendAsync("CurrentPageChanged", pageKey);
    }

    /// <summary>Mute command: mutes or unmutes audio.</summary>
    public async Task SetAudioMuted(bool isMuted)
    {
        roomState.SetAudioMuted(isMuted);
        await Clients.All.SendAsync("AudioMutedChanged", isMuted);
    }

    /// <summary>
    /// Source routing command: selects the active source. If the system is off, this also starts it up. A source that arrives
    /// over NVX is routed to the display first. The source is selected even if the NVX fails (it may be offline), so the rest
    /// of the room keeps working; the stream is then reported as not running.
    /// </summary>
    public async Task SelectSource(string sourceId)
    {
        if (roomState.SystemPower == SystemPowerState.Off)
        {
            await StartSystemAsync();
        }

        roomState.SelectSource(sourceId);
        await Clients.All.SendAsync("SelectedSourceChanged", sourceId);

        // TODO: Lab 2.5b - Route the selected source over NVX
        // Hint: Only for a source the nvxRouter HasRoute for: try to route it with TryRouteAsync BEFORE changing the room
        //       state. Select the source either way: an offline NVX must not stop the room working. After
        //       SelectedSourceChanged has been sent, record whether the stream is running (whether the route worked) and
        //       broadcast StreamRunningChanged.
        // Ref:  The README.md holds definition of the SignalR API
        // Add your work below this line

        // Lab 2.5b - End
    }

    /// <summary>
    /// Query: the URL of the NVX receiver's preview image, <c>/nvx/preview</c>, relative to the hub's host. Null when the room
    /// has no NVX receiver.
    /// </summary>
    public string? GetPreviewUrl() => nvxRouter.IsConfigured ? NvxPreviewEndpoint.Path : null;

    /// <summary>NVX command: starts or stops the receiver's stream, keeping the routed source.</summary>
    public async Task SetStreamRunning(bool isRunning)
    {
        if (!nvxRouter.IsConfigured)
        {
            throw new HubException("This room has no NVX receiver.");
        }

        await RunNvxAsync(isRunning ? "Could not start the stream" : "Could not stop the stream",
            () => nvxRouter.SetStreamRunningAsync(isRunning, Context.ConnectionAborted));

        roomState.SetStreamRunning(isRunning);
        await Clients.All.SendAsync("StreamRunningChanged", isRunning);
    }

    /// <summary>NVX command: turns the receiver's preview images on or off.</summary>
    public async Task SetPreviewEnabled(bool isEnabled)
    {
        if (!nvxRouter.IsConfigured)
        {
            throw new HubException("This room has no NVX receiver.");
        }

        await RunNvxAsync(isEnabled ? "Could not turn the preview on" : "Could not turn the preview off",
            () => nvxRouter.SetPreviewEnabledAsync(isEnabled, Context.ConnectionAborted));

        await Clients.All.SendAsync("PreviewEnabledChanged", isEnabled);
    }

    // Routes an NVX source, returning whether it worked. A failure is logged (see LogNvxFailure) but never thrown: the source
    // is selected whether or not the NVX is there.
    private async Task<bool> TryRouteAsync(string sourceId)
    {
        try
        {
            return await nvxRouter.RouteSourceAsync(sourceId, Context.ConnectionAborted);
        }
        catch (NvxException exception)
        {
            LogNvxFailure($"Could not route source \"{sourceId}\"", exception);
            return false;
        }
    }

    // Runs an NVX call. A failure is logged (see LogNvxFailure), then turned into a HubException so the client sees why,
    // with the NVX's own message included.
    private async Task RunNvxAsync(string failure, Func<Task> nvxCall)
    {
        try
        {
            await nvxCall();
        }
        catch (NvxException exception)
        {
            LogNvxFailure(failure, exception);
            throw new HubException($"{failure}: {exception.Message}");
        }
    }

    // An NVX that refused a request is logged as an error (on a processor: the Crestron error log, through the program's
    // logger factory). An offline NVX is only logged at debug level: its going offline and coming back are each logged once
    // by the web host, so a panel used while it is offline does not fill the error log.
    private void LogNvxFailure(string failure, NvxException exception)
    {
        if (exception is NvxUnreachableException)
        {
            logger.LogDebug("{Failure}: {Reason}", failure, exception.Message);
        }
        else
        {
            logger.LogError(exception, "{Failure}: {Reason}", failure, exception.Message);
        }
    }

    // Runs the Off -> Starting -> On sequence, broadcasting each transition.
    private async Task StartSystemAsync()
    {
        // TODO: Lab 1.5b - Broadcast each step of the power-up sequence (Starting, then On)
        // Hint: Set the SystemPower in the roomState to Starting and broadcast it to all clients with
        //       Then wait for PowerSequeceDuration before setting the SystemPower to On, and broadcast it again.
        //       A simple async await on a Delay is sufficient for this lab
        // Ref:  The README.md holds definition of the SignalR API.
        // Add your work below this line

        // Lab 1.5b - End

        // REF: Lab 1.5b - Broadcast each step of the power-up sequence (Starting, then On)
        roomState.SetSystemPower(SystemPowerState.Starting);
        await Clients.All.SendAsync("SystemPowerStateChanged", SystemPowerState.Starting);

        await Task.Delay(PowerSequenceDuration);

        roomState.SetSystemPower(SystemPowerState.On);
        await Clients.All.SendAsync("SystemPowerStateChanged", SystemPowerState.On);
        // REF: Lab 1.5b - End
    }

    // Runs the On -> Stopping -> Off sequence, broadcasting each transition.
    private async Task StopSystemAsync()
    {
        // TODO: Lab 1.5c - Broadcast each step of the power-off sequence (Stopping, then Off)
        // Hint: Set the SystemPower in the roomState to Stopping and broadcast it to all clients with
        //       Then wait for PowerSequeceDuration before setting the SystemPower to Off, and broadcast it again.
        //       A simple async await on a Delay is sufficient for this lab
        // Ref:  The README.md holds definition of the SignalR API.
        // Add your work below this line

        // Lab 1.5c - End

        // REF: Lab 1.5c - Broadcast each step of the power-off sequence (Stopping, then Off)
        roomState.SetSystemPower(SystemPowerState.Stopping);
        await Clients.All.SendAsync("SystemPowerStateChanged", SystemPowerState.Stopping);

        await Task.Delay(PowerSequenceDuration);

        roomState.SetSystemPower(SystemPowerState.Off);
        await Clients.All.SendAsync("SystemPowerStateChanged", SystemPowerState.Off);
        // REF: Lab 1.5c - End
    }
}

/// <summary>Request body for <see cref="RoomHub.SelectPreset"/>.</summary>
/// <param name="PresetId">Id of the <see cref="LightingPresetConfig"/> to recall.</param>
public sealed record SelectPresetRequest([property: Newtonsoft.Json.JsonProperty("presetId")] string PresetId);

/// <summary>Request body for <see cref="RoomHub.SelectShadePreset"/>.</summary>
/// <param name="PresetId">Id of the <see cref="ShadePresetConfig"/> to recall.</param>
public sealed record SelectShadePresetRequest([property: Newtonsoft.Json.JsonProperty("presetId")] string PresetId);

/// <summary>Result of <see cref="RoomHub.GetRoomState"/>: everything a client needs to show the room as it is now.</summary>
/// <param name="SystemPower">Current power sequence state.</param>
/// <param name="VolumeLevelPercent">Volume, 0 to 100.</param>
/// <param name="IsAudioMuted">Whether audio is muted.</param>
/// <param name="SelectedSourceId">Id of the selected source, or null if none.</param>
/// <param name="CurrentPage">Page clients are synchronised to.</param>
/// <param name="SelectedPresetId">The current scene: id of the last preset recalled, or null if none has been, or a light has been changed on its own since.</param>
/// <param name="LightLevels">Level of every configured light, in configuration order.</param>
/// <param name="ShadePositions">Position of every configured shade, in configuration order.</param>
/// <param name="ShadeTilts">Tilt of every tilt-capable shade, in configuration order.</param>
/// <param name="ActiveShadePresetId">The current shade scene: id of the last shade preset recalled, or null if none has been, or a shade has been changed on its own since.</param>
/// <param name="IsStreamRunning">Whether the NVX receiver's stream is running.</param>
public sealed record RoomStateSnapshot(
    [property: Newtonsoft.Json.JsonProperty("systemPower")] SystemPowerState SystemPower,
    [property: Newtonsoft.Json.JsonProperty("volumeLevelPercent")] int VolumeLevelPercent,
    [property: Newtonsoft.Json.JsonProperty("isAudioMuted")] bool IsAudioMuted,
    [property: Newtonsoft.Json.JsonProperty("selectedSourceId")] string? SelectedSourceId,
    [property: Newtonsoft.Json.JsonProperty("currentPage")] PageKey CurrentPage,
    [property: Newtonsoft.Json.JsonProperty("selectedPresetId")] string? SelectedPresetId,
    [property: Newtonsoft.Json.JsonProperty("lightLevels")] IReadOnlyList<LightLevel> LightLevels,
    [property: Newtonsoft.Json.JsonProperty("shadePositions")] IReadOnlyList<ShadePosition> ShadePositions,
    [property: Newtonsoft.Json.JsonProperty("shadeTilts")] IReadOnlyList<ShadeTilt> ShadeTilts,
    [property: Newtonsoft.Json.JsonProperty("activeShadePresetId")] string? ActiveShadePresetId,
    [property: Newtonsoft.Json.JsonProperty("isStreamRunning")] bool IsStreamRunning);

/// <summary>One light's level in a <see cref="RoomStateSnapshot"/>.</summary>
/// <param name="LightId">Id of the <see cref="LightConfig"/>.</param>
/// <param name="LevelPercent">Level, 0 to 100.</param>
public sealed record LightLevel(
    [property: Newtonsoft.Json.JsonProperty("lightId")] string LightId,
    [property: Newtonsoft.Json.JsonProperty("levelPercent")] int LevelPercent);

/// <summary>One shade's position in a <see cref="RoomStateSnapshot"/>.</summary>
/// <param name="ShadeId">Id of the <see cref="ShadeConfig"/>.</param>
/// <param name="PositionPercent">Position, 0 (closed) to 100 (open).</param>
public sealed record ShadePosition(
    [property: Newtonsoft.Json.JsonProperty("shadeId")] string ShadeId,
    [property: Newtonsoft.Json.JsonProperty("positionPercent")] int PositionPercent);

/// <summary>One shade's tilt in a <see cref="RoomStateSnapshot"/>.</summary>
/// <param name="ShadeId">Id of the <see cref="ShadeConfig"/>.</param>
/// <param name="TiltPercent">Tilt, 0 to 100.</param>
public sealed record ShadeTilt(
    [property: Newtonsoft.Json.JsonProperty("shadeId")] string ShadeId,
    [property: Newtonsoft.Json.JsonProperty("tiltPercent")] int TiltPercent);
