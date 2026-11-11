using Microsoft.AspNetCore.SignalR;
using P709.Core.Configuration;
using P709.Core.Connection;

namespace P709.Web.Hubs;

/// <summary>
/// Hub the front-end connects to. Exposes the configuration query plus commands and their corresponding change events,
/// per the Unified Room Control API.
/// </summary>
public class RoomHub(RoomConfiguration roomConfiguration, RoomState roomState, ILogger<RoomHub> logger) : Hub
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

    // TODO: Lab 1.3 - Hook up the lighting SignalR commands
    // Hint: Add public methods for each of the commands for setting the light level and selecting a preset.
    //       Each method should update the roomState and broadcast the corresponding change event to all clients.
    //       SelectPreset should also record the preset with roomState.SetSelectedPreset, after setting the levels.
    // Add your work below this line

    // Lab 1.3 - End

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

    // TODO: Lab 1.2b - Implement the Hub.OnConnectedAsync override
    // Hint: Implement an override for the Hub<> connection event handler
    //       call the base implementation to maintain the standard behaviour
    //       then send the current SystemPowerState (found in roomState) to the caller.
    // Ref:  Refer to the SignalR API spec in the README.md, and other RoomHub methods that communicate feedback to the UI
    // Add your work below this line

    // Lab 1.2b - End

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
    /// Source routing command: selects the active source. If the system is off, this also starts it up.
    /// </summary>
    public async Task SelectSource(string sourceId)
    {
        if (roomState.SystemPower == SystemPowerState.Off)
        {
            await StartSystemAsync();
        }

        roomState.SelectSource(sourceId);
        await Clients.All.SendAsync("SelectedSourceChanged", sourceId);
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
