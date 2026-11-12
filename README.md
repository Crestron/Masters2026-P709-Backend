# Masters2026 P709

# Program Structure

## Crestron
This is the only part of the program that concerns Crestron specific code.
The purpose of splitting the program into a Crestron project is to isolate the SDK dependency and allow the rest of the code to be tested outside of a Crestron environment.

Changes to the Crestron project are expected to be minimal. The Crestron project is not tested directly.

It is the project that contains the settings file and room config as user files so that the program can be deployed with different configs, without recompiling.

The project:
* Sets up the CrestronSystem
* Loads the settings
* Loads the room configuration
* Registers a touchpanel so that the SignalR hub details can be read
* Starts the web host

Along with the CrestronConsole entry point and scaffolding, there are two concrete implementations for interfaces in the project:

### CrestronHostAddressProvider: IHostAddressProvider

This abstracts the main code from the detail of the host - control system appliance, or VC-4 or test environment.
The purpose of GetHostAddress() is to provide the IP address of the host to the client (touch panel) for connection to the SignalR hub.

### CrestronLoggerProvider: ILoggerProvider

This implements logging for the runtime environment.  This is just an example as there are many ways to implement logging, such as Serilog.

SSH to your VC 4 instance and run the command:

`sudo journalctl -u virtualcontrol.service -f`

## Core

#### Configuration - the "model"

The room configuration structure and an interface to abstract the testing surface from the Crestron project.
It also defines the `RoomState` which is a singleton internal model of the room's state.
Roomconfig is sent to the client as a single object.

#### Connection - bootstrapping

The code that provides the information for the client to connect to the SignalR hub.
This abstracts all of the concrete details from the main code, so that it can be tested outside of a Crestron environment.
Includes:
* Join values (there is only one here)
* The settings structure/deserialisation/defaults
* The IHostAddressProvider and IPanelSerialOutput interfaces: the only two places Core needs something from Crestron (implemented in the Crestron project, faked in the tests)
* The connection details (which build the hub URL) and the publisher that sends them on serial join 1.

#### NVX

Introduced for Lab 2 code for the NVX REST implementation.

### Core.Tests

Tests are small and follow the 3 AAAs pattern - Arrange, Act, Assert as we taught a few years ago.

Core.Tests cover:
* RoomConfig validation
* ConnectionDetails and ConnectionDetailsPublisher
* LabSettings validation
* RoomState change tracking
* NVX: request bodies, reply parsing, the REST client's login/cookie/retry behaviour (against a simulated NVX), routing, and the `nvx` config section

## Web

#### WebHost - the "server"

This is the ASP.NET Core host that provides the SignalR hub and health endpoint.
It created the web server, configures the services (such as the hub, logging, CORS) and maps the endpoints.

#### Hub - the "controller"

This is the SignalR hub that provides the interface to the client.  It is a thin layer that exposes the model and connection details to the client, and provides a way for the client to subscribe to changes in the model.

#### Infrastructure (plumbing)

The ForwardingLoggerProvider is a simple implementation of ILoggerProvider that forwards logging to the CrestronLoggerProvider.  It is used in the WebHost to provide logging for the SignalR hub.

#### NVX

Introduced in Lab 2 code for the NVX REST implementation.

#### MCP

Introduced in Lab 4 code for the MCP implementation.

### Web.Tests

Web.Tests cover:
* RoomHub change tracking
* RoomHub change notification to clients
* RoomHub GetConfig query
* NVX hub commands and events, the `/nvx/preview` endpoint, and what they log (with a fake NVX receiver, so no NVX is needed)
* MCP: `RoomTools` (what each MCP tool reads and does, and the room hub events it sends), the troubleshooting conversation, and the `/hubs/mcp` hub (with a fake troubleshooting session, so no gateway or API key is needed)

## File structure

| Project | Crestron? | Contents |
|---|---|---|
| `src/P709.Core` | No | `Connection/`: join map (`PanelJoins`), `LabSettings`, `ConnectionDetails` and its publisher, `IPanelSerialOutput`, `IHostAddressProvider`. `Configuration/`: `RoomConfig` (`RoomConfiguration` and its `Load`), `RoomConfigValidator`, `JsonConfigFile`. `Nvx/`: `NvxConfig`, `NvxRestClient` (and `INvxClient`), `NvxRequests`/`NvxResponses`, `NvxRouter` |
| `src/P709.Web` | No | ASP.NET Core host (`WebHost`): `RoomHub` at `/hubs/room`, `/health`, `/nvx/preview` (`NvxPreviewEndpoint`), CORS. `Mcp/`: `RoomTools` (the room as the MCP tools see it), `TroubleshootingService` (behind `ITroubleshootingSession`), `McpHub` at `mcpHubPath` (`/hubs/mcp`), `McpHubBroadcaster`, `McpStatus`, `McpHosting` |
| `src/P709.Crestron` | Yes | Thin SIMPL# Pro shell: `ControlSystem`, XPanel adapter, host-address adapter, console/error-log logger. `McpRegistration` (one `McpRoom` call per MCP tool) and `TroubleshootingSessionAdapter`: the only code that references `CrestronAgentLib.SimplSharpPro`. No logic |
| `tests/P709.Core.Tests`, `tests/P709.Web.Tests` | No | xUnit v3 |

- **Testability:** the tests never reference the Crestron project, so they run anywhere `dotnet test` does. Anything worth testing sits behind an interface in Core or Web; the Crestron project only adapts those interfaces to the SDK.
- **JSON:** Newtonsoft.Json throughout, including SignalR hub payloads.

# Getting Going

- Compile the Crestron project - this will also compile the Core and Web projects, and create a .cpz file in the `bin/Debug` folder.
- Create a program on the VC-4 server in the Program Library
- Apply the .cpz and the .ch5z (built from the UI project, added here as an XPanel).
- Zip the two .json files from the Crestron project into a .zip file
- Create a room on the VC-4 server and add the .zip file to the User Files
- The VC-4 logs will show the program starting, and the web host listening on the configured port:
  :
  Nov 02 10:48:18 localhost.localdomain SimplSharpPro[11112]: Control System Initialized (API port 6001)
  :

# Labs

## Student task markers

Each lab task will be marked in the code in the solution with a TODO/END block:

```csharp
// TODO: Lab 1.1 - the aim of the task
// Hint: some guidance for the task
// Add your work below this line

...Your code added here...

// Lab 1.1 - End
```

When we start the next lab, the main repo will be updated to add a reference implementation below the TODO.

You will then comment out either the reference implementation to continue with your own code, or your code to build upon the reference implementation.

```
// REF: Lab 1.1 - the aim of the task
...the solution code...
// REF: Lab 1.1 - End
```

The tasks are listed below:

| Task | Where | Aim | Why |
|---|---|---|---|
| 0.1 | `RoomConfiguration.Load` | Load and validate the room configuration | Students introduced to the project, see how a user file can be loaded and validated |
| 0.2 | `ControlSystem.InitializeSystem` | Start the web host on the configured port | How to start an ASP.NET Core host in a Crestron program |
| 1.1a | `WebHost.Build` | Register SignalR with Newtonsoft.Json | How to configure SignalR to use Newtonsoft.Json for payloads, so that the client can deserialize them |
| 1.1b | `WebHost.Build` | Map the SignalR rooms hub | Plumbing... How to map a SignalR hub to a URL in an ASP.NET Core host |
| 1.1c | `WebHost.Build` | Register the room configuration and state as singletons | How to register a singleton service in an ASP.NET Core host, so that the hub can use it |
| 1.1d | `ControlSystem.OnPanelOnlineStatusChange` | Publish when the panel comes online | How to publish the connection details to the touch panel when it comes online, so that it can connect to the SignalR hub |
| 1.2a | `RoomHub.GetRoomConfiguration` | Query: return the room configuration | How to implement a SignalR hub method that returns the room configuration to the client upon request |
| 1.2b | `RoomHub.OnConnectedAsync` | Send the current power state to a new client | How to send the current state of the room to a new client when it connects, so that it can update its UI to match the room state |
| 1.3 | `RoomHub.SetLightLevel` and `RoomHub.SelectPreset` | Command: set a light level or select a preset, and broadcast the change to every client | More SignalR hub methods, showing how to change the room state and broadcast the change to all clients |
| 1.4 | `RoomHub.SetVolumeLevel` | Command: set the volume in the room state, and broadcast the new value to every client | More SignalR hub methods, showing how to change the room state and broadcast the change to all clients |
| Stretch goal | For anybody with time on their hands | | |
| 1.5a | `UI Project` | Add the shades component to the UI to invoke the existing shades code | |
| 1.5b+c | `RoomHub.StartSystemAsync` | Broadcast each step of the power-up/down sequence to clients asynchronously | Asynchronous broadcasts to clients |

## Connection

A touch panel (XPanel, **IPID 03**) connects through CrComLib. **Serial join 1** returns the connection details for a custom SignalR connection:

```json
{"hubUrl":"http://<processor-ip>:5000/hubs/room"}
```

# SignalR Room Hub API (`/hubs/room`)

This is the contract between the program (C#) and the front-end (Svelte). Both sides are written against it: the hub must offer exactly these names and shapes, and the front-end may rely on nothing else.

## Connecting

- **Where:** the front-end reads `hubUrl` from serial join 1 (see above) and opens a SignalR connection to it. Do not hard-code the address or port.
- **Client library:** on the Svelte side, use Microsoft's SignalR JavaScript client (`@microsoft/signalr`). Its default JSON protocol matches the server.
- **On connect:** the hub immediately sends the new client `SystemPowerStateChanged` with the current power state. Register your event handlers *before* starting the connection, or you may miss it.
- **Start-up sequence:** call `GetRoomConfiguration` to build the UI, then `GetRoomState` to fill it in. From then on, keep it up to date from the events.
- **Liveness:** `GET /health` on the same host and port returns `Healthy` when the web host is running. Use it to tell "the program is down" apart from "my hub code is wrong".

## Conventions

| Topic | Rule |
|---|---|
| Names | Hub method and event names are as listed below. Method names are matched case-insensitively, but use them exactly as written. |
| Arguments | Positional: send them in the order listed. |
| JSON | Property names are camelCase. Enums are sent as strings, using the wire values listed under [Enumerations](#enumerations). |
| Ids | Always strings, even when they look like numbers (`"1"`). |
| Percentages | Whole numbers from 0 to 100. |
| SVG | Every `...Svg` field is base64-encoded SVG markup, ready to use in a `data:image/svg+xml;base64,` URI. |
| Errors | A rejected request fails the call: on the client the invocation fails (the promise rejects) with the server's message. Nothing is broadcast and the room state is unchanged. |
| Source of truth | Commands return no data. The UI should update from the **events**, not from what it sent, so every connected panel shows the same thing. |

## Queries

| Method | Arguments | Returns |
|---|---|---|
| `GetRoomConfiguration` | none | The [room configuration](#room-configuration): everything needed to build the UI. |
| `GetRoomState` | none | A [room state snapshot](#room-state-snapshot): the room as it is now. Call it after connecting, and again after reconnecting. |
| `GetPreviewUrl` | none | The [preview image](#nvx-preview-images) path, `/nvx/preview`, or `null` if the room has no NVX receiver. |

## Commands

| Method | Arguments | Effect | Events raised | Who receives them |
|---|---|---|---|---|
| `SetLightLevel` | `lightId: string`, `levelPercent: int` | Sets one light, and clears the current scene (the lights may no longer match the preset). | `LightLevelChanged` | Every client, including the sender |
| `SelectPreset` | `{ presetId: string }` (one object, not a bare string) | Applies each light level in the preset and makes it the current scene. Fails on an unknown preset id. | `LightLevelChanged` once per light in the preset, then `PresetSelected` | Every client, including the sender |
| `SetShadePosition` | `shadeId: string`, `positionPercent: int` | Moves one shade: 0 is closed, 100 is open. Clears the active shade scene (the shades may no longer match the preset). Fails on an unknown shade id or a position outside 0-100. | `ShadePositionChanged`, then `ShadePresetSelected` with `null` if a shade preset was active | Every client, including the sender |
| `SetShadeTilt` | `shadeId: string`, `tiltPercent: int` | Tilts one shade's slats, 0 to 100. Clears the active shade scene. Fails on an unknown shade id, a shade that does not support tilt, or a tilt outside 0-100. | `ShadeTiltChanged`, then `ShadePresetSelected` with `null` if a shade preset was active | Every client, including the sender |
| `SelectShadePreset` | `{ presetId: string }` (one object, not a bare string) | Applies each target's position (and tilt, if given) in the shade preset and makes it the active shade scene. Fails on an unknown preset id. | `ShadePositionChanged`/`ShadeTiltChanged` once per target in the preset, then `ShadePresetSelected` | Every client, including the sender |
| `SetSystemPower` | `isOn: bool` | Runs the power sequence. Ignored if the system is already on its way to (or at) the requested state. The call completes only when the sequence ends, after about 10 seconds. | `SystemPowerStateChanged` twice: `starting` then `on`, or `stopping` then `off` | Every client, including the sender |
| `SelectSource` | `sourceId: string` | Selects a source. If the system is off, it is powered on first. A source in the `nvx` configuration is routed first: the receiver's stream location is set to `rtsp://<multicast address>` and the stream is started, and the receiver's previews are turned on. If the NVX fails, the call fails and nothing changes. | Power events as above, if needed, then `SelectedSourceChanged`, then `StreamRunningChanged` (`true`, NVX sources only) | Every client, including the sender |
| `SetStreamRunning` | `isRunning: bool` | Starts or stops the NVX receiver's stream, keeping the routed source. Fails if the room has no NVX or the NVX fails. | `StreamRunningChanged` | Every client, including the sender |
| `SetPreviewEnabled` | `isEnabled: bool` | Turns the NVX receiver's preview images on or off. Fails if the room has no NVX or the NVX fails. | `PreviewEnabledChanged` | Every client, including the sender |
| `SetVolumeLevel` | `levelPercent: int` | Sets the volume. | `VolumeLevelChanged` | Every client, including the sender |
| `SetAudioMuted` | `isMuted: bool` | Mutes or unmutes audio. | `AudioMutedChanged` | Every client, including the sender |
| `SetCurrentPage` | `pageKey: PageKey` | Tells other panels which page to show. | `CurrentPageChanged` | Every client **except** the sender (it already knows) |

## Events

| Event | Arguments |
|---|---|
| `SystemPowerStateChanged` | `state: SystemPowerState` |
| `LightLevelChanged` | `lightId: string`, `levelPercent: int` |
| `PresetSelected` | `presetId: string` |
| `ShadePositionChanged` | `shadeId: string`, `positionPercent: int` |
| `ShadeTiltChanged` | `shadeId: string`, `tiltPercent: int` |
| `ShadePresetSelected` | `presetId: string or null` |
| `SelectedSourceChanged` | `sourceId: string` |
| `VolumeLevelChanged` | `levelPercent: int` |
| `AudioMutedChanged` | `isMuted: bool` |
| `CurrentPageChanged` | `pageKey: PageKey` |
| `StreamRunningChanged` | `isRunning: bool` |
| `PreviewEnabledChanged` | `isEnabled: bool` |

Clearing the current scene does not raise an event of its own: a client that shows the current scene should clear it when it receives a `LightLevelChanged` that is not part of a preset recall (one followed by `PresetSelected`). The same applies to the shade scene: a client should clear it when it receives a `ShadePositionChanged` or `ShadeTiltChanged` not followed by `ShadePresetSelected`, or on an explicit `ShadePresetSelected` with `null`.

## Room state snapshot

Returned by `GetRoomState`. It holds the same values the events report, all at once.

| Property | Type | Notes |
|---|---|---|
| `systemPower` | `SystemPowerState` | |
| `volumeLevelPercent` | int | |
| `isAudioMuted` | bool | |
| `selectedSourceId` | string or null | Null until a source has been selected. |
| `currentPage` | `PageKey` | |
| `selectedPresetId` | string or null | The current scene: the last preset recalled. Null if none has been recalled, or a light has been changed on its own since. |
| `lightLevels` | list of `{ lightId, levelPercent }` | Every configured light, in configuration order. A light never set reports `0`. |
| `shadePositions` | list of `{ shadeId, positionPercent }` | Every configured shade, in configuration order. A shade never moved reports `0` (closed). |
| `shadeTilts` | list of `{ shadeId, tiltPercent }` | Every tilt-capable shade (`supportsTilt: true`), in configuration order. A shade never tilted reports `0`. |
| `activeShadePresetId` | string or null | The active shade scene: the last shade preset recalled. Null if none has been recalled, or a shade has been changed on its own since. |
| `isStreamRunning` | bool | Whether the NVX receiver's stream is running. `false` until a source has been routed or `SetStreamRunning` has been called. |

## Room configuration

Returned by `GetRoomConfiguration`. It is loaded from `P709.roomconfig.json` at start-up and does not change while the program runs.

| Property | Type | Notes |
|---|---|---|
| `configurationVersion` | int | Increases whenever this shape changes. The front-end can check it to detect a mismatch. |
| `roomName` | string | Display name. |
| `roomSvg` | string (base64 SVG), optional | Plan of the room, which the lights are drawn over. |
| `sources` | list of `{ id, name, type: SourceType, iconSvg? }` | In display order. With no `iconSvg`, the front-end chooses an icon from the `type`. |
| `lights` | list of `{ id, name, type: LightType, iconSvg, positions: [{ xPercent, yPercent }] }` | Positions are percentages across and down the room image, so they suit any rendered size. A light may appear in several places. |
| `lightingPresets` | list of `{ id, name, levels: [{ lightId, levelPercent }] }` | Lighting presets for `SelectPreset`. |
| `shades` | list of `{ id, name, type: ShadeType, supportsTilt? }` | In display order. `supportsTilt` defaults to `false`; when `true`, the shade accepts `SetShadeTilt` and tilt targets in shade presets. |
| `shadePresets` | list of `{ id, name, targets: [{ shadeId, positionPercent, tiltPercent? }] }`, optional | Shade presets for `SelectShadePreset`. `tiltPercent` is only valid for a target whose shade has `supportsTilt: true`. |
| `mcpHubPath` | string, optional | Path of the MCP hub (`/hubs/mcp`): AI troubleshooting and the MCP registration status. See [MCP Hub API](#signalr-mcp-hub-api-hubsmcp). Not mapped when absent. |
| `nvx` | `{ username, receiverAddress, sources: [{ sourceId, multicastAddress }] }`, optional | The DM-NVX routing: only the receiver is controlled; each source's transmitter streams to its multicast address on its own. The `password` is read from the file but never sent to the front-end. Sources not listed here are selected without NVX routing. |

## NVX preview images

`GET /nvx/preview` on the same host and port as the hub returns the NVX receiver's latest preview (a snapshot of what the display is being sent) as `image/jpeg`, with `Cache-Control: no-store`.

- **Why a proxy:** the image lives on the NVX receiver, over HTTPS with a self-signed certificate and behind a cookie login. A panel cannot load it directly (and browsers block `user:password@` credentials on images). The program holds a logged-in session, so it fetches the largest available image and passes it on.
- **URL:** `GetPreviewUrl` gives the path; resolve it against the hub URL's origin (`new URL(previewUrl, hubUrl)`). Add a changing query string (for example `?t=<timestamp>`) to refresh the `<img>`, for example after `SelectedSourceChanged`.
- **Responses:** `200` with the image; `404` if the room has no NVX receiver; `503` if previews are turned off or no image is ready yet; `502` if the NVX could not be reached or refused the request.
- **Turning previews on:** `SelectSource` turns them on when it routes an NVX source; `SetPreviewEnabled` turns them on or off. Fetching a preview never changes the setting.
- **One preview:** only the receiver is controlled, so there is one preview of whatever it is showing, not one per source.

## Enumerations

| Type | Wire values |
|---|---|
| `SystemPowerState` | `off`, `starting`, `on`, `stopping` |
| `PageKey` | `home`, `lights`, `shades`, `sources`, `support` |
| `SourceType` | `tv`, `ohp`, `wirelessPresenter`, `laptopHdmi`, `laptopUsbC`, `other` |
| `LightType` | `dimmer` (any level 0-100), `switch` (0 or 100 only) |
| `ShadeType` | `roller`, `curtain`, `venetian` |

# SignalR MCP Hub API (`/hubs/mcp`)

Event names are constants on `McpHub`; the state is one record, `TroubleshootingState`.

The hub at the room configuration's `mcpHubPath` carries MCP-related communication only: the AI troubleshooting conversation and the MCP Gateway registration status. Room control stays on `/hubs/room`; when the AI changes the room through an MCP tool, the panel hears about it there, through the usual room hub events. Same conventions as the room hub: Newtonsoft.Json, camelCase.

There is one conversation per room, shared by every panel. A panel that opens troubleshooting part-way sees the conversation already running.

| Member | Kind | Notes |
|---|---|---|
| `GetTroubleshootingState()` | query | Returns the `TroubleshootingState` below. |
| `StartTroubleshooting()` | command | Starts a conversation: Claude looks at the room through the MCP tools, then replies. Returns at once; progress arrives as `TroubleshootingStateChanged`. Ignored while `isBusy`, or while `isAvailable` is false. |
| `AnswerTroubleshooting(optionIndex)` | command | Answers with `answerOptions[optionIndex]`. Returns at once, like Start. Ignored while `isBusy`, or for an index that is not a current option. |
| `GetMcpStatus()` | query | The gateway registration state: `NotStarted` (MCP off or not started yet), `NotRegistered`, `Registering`, `Registered`, `DuplicateRoomName`, `DuplicateRegistrationId`, `Error`, `Deregistered`. |
| `TroubleshootingStateChanged(state)` | event | Sent to every MCP hub client after every change, with the whole state. |
| `McpStatusChanged(status)` | event | Sent to every MCP hub client when the registration state changes. |

`TroubleshootingState`:

| Property | Type | Notes |
|---|---|---|
| `isAvailable` | bool | False when no Anthropic API key is configured, or MCP is off. |
| `isBusy` | bool | Claude is working on a turn. |
| `status` | string | A status line: progress, the answer just chosen, `Anthropic API key not configured`, or `Troubleshooting failed: <reason>`. |
| `message` | string | Claude's latest message. Empty while Claude works on the next one. |
| `answerOptions` | list of string | The answers to offer, in order. |
| `selectedAnswerIndex` | int or null | The answer just chosen. |
| `isDone` | bool | Claude considers the conversation finished. |

# MCP

`McpRegistration` registers these tools with the MCP Gateway running on the VC-4 or processor. Each reads or commands the room through `RoomTools`; items are indexed from 0, in room-configuration order. Tool names and descriptions follow the McpStarter example program.

| Tool | Kind | What it does |
|---|---|---|
| *(built-in)* | power | System power: reads as on only when `on`; on and off run the same 10-second sequence as the room hub |
| `Light_Level_Feedback` / `Light_Level_Trigger` / `Light_Level_Names` | analog get/set (one item per light), names | Light levels 0-100. A `switch` light goes fully on from 50 |
| `Light_Level_Preset_Fb` / `Light_Level_Preset_Trigger` / `Light_Level_Preset_Names` | digital get/trigger (one item per preset), names | Lighting presets (stretch goal 4.6) |
| `Volume_Level_Fb` / `Volume_Level_Trigger`, `Volume_Mute_Fb` / `Volume_Mute_Trigger` | analog, digital | Volume 0-100 and mute (stretch goal 4.6) |
| `video_routing_main` | video routing | Selects a source as `SelectSource` does, including the NVX route (stretch goal 4.7). Source types: wirelessPresenter 410, laptop 10, tv 230, other 20 |

Shades, climate, occupancy and tickets are not exposed.

## MCP settings

MCP is configured in `P709.settings.json`, not the room configuration: the room configuration is sent to the panel as it is, and the API key must never reach it.

```json
{
  "apiPort": 6001,
  "mcp": {
    "registrationId": 1,
    "description": "Conference room with five sources (two over DM-NVX), three dimmable lights and a switched whiteboard light, with lighting presets, and program volume.",
    "anthropicApiKey": "YOUR-ANTHROPIC-API-KEY"
  }
}
```

| Property | Type | Notes |
|---|---|---|
| `mcp` | object, optional | Leave it out and MCP is not started: the room works exactly as without Lab 4. |
| `mcp.registrationId` | int, 1 or more | Unique within the program. Defaults to 1. |
| `mcp.description` | string, required | A summary of the room for the AI. |
| `mcp.anthropicApiKey` | string, optional | For AI troubleshooting. Blank or the placeholder: the MCP tools work, troubleshooting reports itself unavailable. The model comes from the toolkit's `/user/crestron_mcptoolkit_config.json`. |

The MCP Gateway must be running on the VC-4 or processor (by default at `http://127.0.0.1:40001`). Watch the console for `MCP registration: Registered`.
