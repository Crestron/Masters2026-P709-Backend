using System.Collections.Generic;

namespace P709.Core.Configuration;

/// <summary>
/// In-memory runtime state of the room, separate from <see cref="RoomConfiguration"/> which describes the UI shape.
/// <see cref="RoomHub"/>-style command handlers update this and broadcast the corresponding change event; newly
/// connected clients are sent the current <see cref="SystemPower"/> so they can reflect it without waiting for a change.
/// </summary>
public sealed class RoomState
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, int> _lightLevels = [];
    private readonly Dictionary<string, int> _shadePositions = [];
    private readonly Dictionary<string, int> _shadeTilts = [];

    /// <summary>Current overall power sequence state.</summary>
    public SystemPowerState SystemPower { get; private set; } = SystemPowerState.Off;

    /// <summary>Current volume level, 0 to 100.</summary>
    public int VolumeLevelPercent { get; private set; }

    /// <summary>Whether audio is currently muted.</summary>
    public bool IsAudioMuted { get; private set; }

    /// <summary>Id of the currently selected source, or null if none is selected.</summary>
    public string? SelectedSourceId { get; private set; }

    /// <summary>Whether the NVX receiver's stream is running. Starts false: the program has not started it yet.</summary>
    public bool IsStreamRunning { get; private set; }

    /// <summary>Key of the page clients should currently be synchronized to.</summary>
    public PageKey CurrentPage { get; private set; } = PageKey.Home;

    /// <summary>Current level, 0 to 100, of each light that has been set, keyed by light id.</summary>
    public IReadOnlyDictionary<string, int> LightLevels
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<string, int>(_lightLevels);
            }
        }
    }

    /// <summary>Current position, 0 (closed) to 100 (open), of each shade that has been set, keyed by shade id.</summary>
    public IReadOnlyDictionary<string, int> ShadePositions
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<string, int>(_shadePositions);
            }
        }
    }

    /// <summary>Current tilt, 0 to 100, of each tilt-capable shade that has been set, keyed by shade id.</summary>
    public IReadOnlyDictionary<string, int> ShadeTilts
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<string, int>(_shadeTilts);
            }
        }
    }

    /// <summary>
    /// The current shade scene: id of the last shade preset recalled, or null if none has been, or a shade has been changed on
    /// its own since (the shades may no longer match the preset).
    /// </summary>
    public string? SelectedShadePresetId { get; private set; }

    /// <summary>Sets <see cref="SystemPower"/>.</summary>
    public void SetSystemPower(SystemPowerState state)
    {
        lock (_gate)
        {
            SystemPower = state;
        }
    }

    /// <summary>
    /// The current scene: id of the last preset recalled, or null if none has been, or a light has been changed on its own since
    /// (the lights may no longer match the preset).
    /// </summary>
    public string? SelectedPresetId { get; private set; }

    /// <summary>
    /// Sets the level of the light identified by <paramref name="lightId"/>, tracked in <see cref="LightLevels"/>.
    /// This clears <see cref="SelectedPresetId"/>, so when recalling a preset call <see cref="SetSelectedPreset"/> after setting its levels.
    /// </summary>
    public void SetLightLevel(string lightId, int levelPercent)
    {
        lock (_gate)
        {
            _lightLevels[lightId] = levelPercent;
            SelectedPresetId = null;
        }
    }

    /// <summary>Sets <see cref="SelectedPresetId"/>, or clears it when <paramref name="presetId"/> is null.</summary>
    public void SetSelectedPreset(string? presetId)
    {
        lock (_gate)
        {
            SelectedPresetId = presetId;
        }
    }

    /// <summary>
    /// Sets the position of the shade identified by <paramref name="shadeId"/>, tracked in <see cref="ShadePositions"/>.
    /// This clears <see cref="SelectedShadePresetId"/>, so when recalling a shade preset call <see cref="SetSelectedShadePreset"/>
    /// after setting its targets.
    /// </summary>
    public void SetShadePosition(string shadeId, int positionPercent)
    {
        lock (_gate)
        {
            _shadePositions[shadeId] = positionPercent;
            SelectedShadePresetId = null;
        }
    }

    /// <summary>
    /// Sets the tilt of the shade identified by <paramref name="shadeId"/>, tracked in <see cref="ShadeTilts"/>.
    /// This clears <see cref="SelectedShadePresetId"/>, so when recalling a shade preset call <see cref="SetSelectedShadePreset"/>
    /// after setting its targets.
    /// </summary>
    public void SetShadeTilt(string shadeId, int tiltPercent)
    {
        lock (_gate)
        {
            _shadeTilts[shadeId] = tiltPercent;
            SelectedShadePresetId = null;
        }
    }

    /// <summary>Sets <see cref="SelectedShadePresetId"/>, or clears it when <paramref name="presetId"/> is null.</summary>
    public void SetSelectedShadePreset(string? presetId)
    {
        lock (_gate)
        {
            SelectedShadePresetId = presetId;
        }
    }

    /// <summary>Sets <see cref="VolumeLevelPercent"/>.</summary>
    public void SetVolumeLevel(int levelPercent)
    {
        lock (_gate)
        {
            VolumeLevelPercent = levelPercent;
        }
    }

    /// <summary>Sets <see cref="IsAudioMuted"/>.</summary>
    public void SetAudioMuted(bool isMuted)
    {
        lock (_gate)
        {
            IsAudioMuted = isMuted;
        }
    }

    /// <summary>Sets <see cref="SelectedSourceId"/>, or clears it when <paramref name="sourceId"/> is null.</summary>
    public void SelectSource(string? sourceId)
    {
        lock (_gate)
        {
            SelectedSourceId = sourceId;
        }
    }

    /// <summary>Sets <see cref="IsStreamRunning"/>.</summary>
    public void SetStreamRunning(bool isRunning)
    {
        lock (_gate)
        {
            IsStreamRunning = isRunning;
        }
    }

    /// <summary>Sets <see cref="CurrentPage"/>.</summary>
    public void SetCurrentPage(PageKey pageKey)
    {
        lock (_gate)
        {
            CurrentPage = pageKey;
        }
    }
}
