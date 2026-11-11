using CrestronAgentLib.SimplSharpPro;
using Microsoft.Extensions.Logging;
using P709.Web.Mcp;

namespace P709.Crestron;

/// <summary>
/// Registers the room's MCP tools with the gateway: one <see cref="McpRoom"/> call per tool, each reading or commanding the
/// room through <see cref="RoomTools"/>. This file only maps; what each tool does lives in <see cref="RoomTools"/>, where it is
/// tested. Tool names and descriptions follow the McpStarter example, so an AI client sees the same tools from either program.
/// </summary>
/// <remarks>
/// P709's devices are its <c>RoomState</c>, not Crestron Sigs, so every tool uses the lambda overloads: <c>() =&gt; value</c> to
/// read, an <see cref="Action"/> to command. The gateway calls commands synchronously; each one starts the room command and
/// returns, and the gateway reads the feedback tool back after the settling time given.
/// </remarks>
internal static class McpRegistration
{
    /// <summary>Registers every tool on <paramref name="room"/>. Call before <see cref="McpRoom.StartAsync"/>.</summary>
    public static void Register(McpRoom room, RoomTools tools, ILogger logger)
    {
        // Starts a room command without waiting for it; a failure (an index the room does not have, an NVX that cannot be
        // reached) is logged, and the AI sees the result through the feedback tool.
        void Run(string tool, Func<Task> command) => _ = Task.Run(async () =>
        {
            try
            {
                await command();
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "MCP {Tool} failed: {Reason}", tool, exception.Message);
            }
        });

        var lights = Enumerable.Range(0, tools.LightNames.Count).ToList();
        var presets = Enumerable.Range(0, tools.PresetNames.Count).ToList();
        var sources = Enumerable.Range(0, tools.SourceNames.Count).ToList();

        // Worked example: the system power. McpRoom has built-in power tools, so this only says how to read the power and what
        // to do when the AI asks for it on or off. Every command is started with Run, so the gateway is never kept waiting.
        room.ReadSystemPower = tools.IsSystemOn;
        room.SystemPowerOnRequested += () => Run("system power on", () => tools.SetSystemPowerAsync(true));
        room.SystemPowerOffRequested += () => Run("system power off", () => tools.SetSystemPowerAsync(false));

        // TODO: Lab 4.3 - Let the AI read and set the light levels
        // Hint: One tool can cover every light: pass a list with one getter (or one setter) per light, and the light names as
        //       the item names. Add:
        //       - an analog getter "Light_Level_Feedback": one () => tools.GetLightLevel(i) per light, unit "light_percent";
        //       - an analog trigger "Light_Level_Trigger": one level => Run(...) per light calling tools.SetLightLevelAsync(i, level),
        //         range 0 to 100, unit "light_percent", read back through "Light_Level_Feedback" after 3000 ms;
        //       - a serial getter "Light_Level_Names" that lists tools.LightNames.
        //       The lights list above holds the light indexes. Descriptions are what the AI reads, so say what each tool does.
        // Ref:  The README of the CrestronAgentLib.SimplSharpPro package (packages/), "Binding to Your Signals"
        // Add your work below this line

        // Lab 4.3 - End

        // REF: Lab 4.3 - Let the AI read and set the light levels
        room.AddAnalogGetter("Light_Level_Feedback", "Returns the light level feedback from 0d to 100d",
            lights.Select(i => (Func<ushort>)(() => tools.GetLightLevel(i))).ToList(),
            unit: "light_percent", itemNames: tools.LightNames);

        room.AddAnalogTrigger("Light_Level_Trigger", "Set the light level value for a specific light",
            lights.Select(i => (Action<ushort>)(level => Run("Light_Level_Trigger", () => tools.SetLightLevelAsync(i, level)))).ToList(),
            0, 100, "light_percent", "Light_Level_Feedback", 3000, tools.LightNames);

        room.AddSerialGetter("Light_Level_Names",
            "The names (in order) for the light levels that are exposed through the Trigger and Fb modules", tools.LightNames);
        // REF: Lab 4.3 - End

        // TODO: Lab 4.6 (Stretch goal) - Let the AI recall lighting presets, and set the volume and mute
        // Hint: Presets work like Lab 4.3, but digital: a getter "Light_Level_Preset_Fb" (is each preset the current scene?), a
        //       trigger "Light_Level_Preset_Trigger" (one Action per preset, no "off" actions, read back after 3000 ms) and a
        //       serial getter "Light_Level_Preset_Names". Volume and mute are single-item tools: "Volume_Level_Fb" and
        //       "Volume_Level_Trigger" (0 to 100, unit "volume_percent", 1000 ms), "Volume_Mute_Fb" and "Volume_Mute_Trigger" (500 ms).
        // Add your work below this line

        // Lab 4.6 - End

        // REF: Lab 4.6 (Stretch goal) - Let the AI recall lighting presets, and set the volume and mute
        room.AddDigitalGetter("Light_Level_Preset_Fb", "Return light level preset feedback status",
            presets.Select(i => (Func<bool>)(() => tools.IsPresetSelected(i))).ToList(), tools.PresetNames);

        room.AddDigitalTrigger("Light_Level_Preset_Trigger", "Trigger a specific lighting preset",
            presets.Select(i => (Action)(() => Run("Light_Level_Preset_Trigger", () => tools.SelectPresetAsync(i)))).ToList(),
            feedbackTool: "Light_Level_Preset_Fb", maxTimeToExecuteMs: 3000, itemNames: tools.PresetNames);

        room.AddSerialGetter("Light_Level_Preset_Names",
            "The names (in order) for the light presets that are exposed through the Trigger and Fb modules", tools.PresetNames);

        room.AddAnalogGetter("Volume_Level_Fb", "Returns the current volume level in the room", tools.GetVolumeLevel, "volume_percent");
        room.AddAnalogTrigger("Volume_Level_Trigger", "Sets the volume level in the room",
            level => Run("Volume_Level_Trigger", () => tools.SetVolumeLevelAsync(level)),
            0, 100, "volume_percent", "Volume_Level_Fb", 1000);

        room.AddDigitalGetter("Volume_Mute_Fb", "Returns the current state of volume mute in the room", tools.IsAudioMuted);

        room.AddDigitalTrigger("Volume_Mute_Trigger", "Turns the volume mute on or off in the room",
            isMuted => Run("Volume_Mute_Trigger", () => tools.SetAudioMutedAsync(isMuted)), "Volume_Mute_Fb", 500);
        // REF: Lab 4.6 - End

        // TODO: Lab 4.7 (Stretch goal) - Let the AI route sources to the display
        // Hint: AddVideoRouting describes a whole display in one call: "video_routing_main", display "Main display", the source
        //       names and types from RoomTools, a signal-detected function per source (P709 has no sync feedback: always true),
        //       the current source index, display power and online (the system power, and always true), power on/off actions
        //       and a select-source action. Start every command with Run.
        // Add your work below this line

        // Lab 4.7 - End

        // REF: Lab 4.7 (Stretch goal) - Let the AI route sources to the display
        room.AddVideoRouting("video_routing_main",
            "Video source routing control, but also signal detection and display status", "Main display",
            sourceNames: tools.SourceNames,
            signalDetected: sources.Select(_ => (Func<bool>)(() => true)).ToList(),
            sourceTypes: tools.SourceTypes,
            currentSourceIndex: tools.GetCurrentSourceIndex,
            displayPower: tools.IsSystemOn,
            displayOnline: () => true,
            displayPowerOn: () => Run("video_routing_main power on", () => tools.SetSystemPowerAsync(true)),
            displayPowerOff: () => Run("video_routing_main power off", () => tools.SetSystemPowerAsync(false)),
            selectSource: index => Run("video_routing_main select", () => tools.SelectSourceAsync(index)));
        // REF: Lab 4.7 - End
    }
}
