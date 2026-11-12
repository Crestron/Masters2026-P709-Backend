using Crestron.SimplSharp;
using Crestron.SimplSharpPro;
using Crestron.SimplSharpPro.UI;
using CrestronAgentLib.Logging;
using CrestronAgentLib.SimplSharpPro;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using P709.Core.Configuration;
using P709.Core.Connection;
using P709.Web;
using P709.Web.Mcp;
using CrestronThread = Crestron.SimplSharpPro.CrestronThread.Thread;
using CrestronDirectory = Crestron.SimplSharp.CrestronIO.Directory;

namespace P709.Crestron;

/// <summary>
/// Thin Crestron shell: registers the touch panel, hosts the web server and publishes the SignalR connection details.
/// All decisions live in <c>P709.Core</c> and <c>P709.Web</c>, where they are unit tested without a Crestron runtime.
/// </summary>
public class ControlSystem : CrestronControlSystem
{
    private readonly CancellationTokenSource _shutdown = new();

    private ILoggerFactory? _loggerFactory;
    private XpanelForSmartGraphics? _panel;
    private ConnectionDetailsPublisher? _publisher;
    private McpRoom? _mcpRoom;

    /// <summary>Initialises the control system. Must return quickly.</summary>
    public ControlSystem()
    {
        try
        {
            CrestronThread.MaxNumberOfUserThreads = 20;
            CrestronEnvironment.ProgramStatusEventHandler += OnProgramStatusEvent;
        }
        catch (Exception exception)
        {
            CrestronConsole.PrintLine("Control System Initialization Failed: " + exception.Message);
        }
    }

    /// <summary>Registers the panel and starts the web host.</summary>
    public override void InitializeSystem()
    {
        _loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new CrestronLoggerProvider()));

        // Both files are configuration, not code, and sit in the room's user folder so they can change without a rebuild.
        // They are loaded the same way: a missing or invalid file is logged with what to fix, and the program stops.
        LabSettings settings;
        RoomConfiguration roomConfiguration;

        try
        {
            // The room's user folder, where VC-4 extracts the room's User File zip.
            var userFolder = Path.Combine(CrestronDirectory.GetApplicationRootDirectory(), "user");

            settings = LabSettings.Load(userFolder);
            roomConfiguration = RoomConfiguration.Load(userFolder);
        }
        catch (Exception exception) when (exception is FileNotFoundException or InvalidConfigurationException)
        {
            ErrorLog.Error("Configuration not loaded, web host not started: {0}", exception.Message);
            return;
        }

        // Register the touch panel on IPID 03 and set up the connection details publisher
        _panel = new XpanelForSmartGraphics(PanelJoins.PanelIpId, this);
        _publisher = new ConnectionDetailsPublisher(settings.ApiPort, new CrestronHostAddressProvider(), new XPanelSerialOutput(_panel));

        _panel.OnlineStatusChange += OnPanelOnlineStatusChange;

        if (_panel.Register() != eDeviceRegistrationUnRegistrationResponse.Success)
        {
            ErrorLog.Error("XPanel registration failed: {0}", _panel.RegistrationFailureReason);
        }

        var loggerFactory = _loggerFactory;
        var cancellationToken = _shutdown.Token;

        // What the web host needs to connect MCP. Null leaves MCP off; the room works exactly as before.
        McpHosting? mcpHosting = null;

        if (settings.Mcp is { } mcp)
        {
            try
            {
                // TODO: Lab 4.1 - Register the room with the MCP Gateway
                // Hint: Create an McpRoom and keep it in _mcpRoom. McpRoom.Create takes this program's number
                //       (InitialParametersClass.ApplicationNumber), the registration id and description from the mcp settings,
                //       15000 ms for the system to turn on, and McpLogLevel.Info. Everything else (the tools, the troubleshooting
                //       sessions, connecting to the gateway) is set up from it below and in StartMcp.
                // Ref:  The README of the CrestronAgentLib.SimplSharpPro package (packages/), "Quick Start"
                // Add your work below this line

                // Lab 4.1 - End

                if (_mcpRoom is { } mcpRoom)
                {
                    mcpHosting = CreateMcpHosting(mcpRoom, mcp, roomConfiguration.RoomName);
                }
            }
            catch (Exception exception)
            {
                // MCP is an extra: without it the room must still start, exactly as before Lab 4.
                ErrorLog.Error("MCP not started: {0}", exception.Message);
                _mcpRoom = null;
                mcpHosting = null;
            }
        }

        // TODO: Lab 0.2 - Start the non-Crestron web host on the configured port
        // Hint: The control system should spawn a new long-running task.  This will call our Web project
        //       static WebHost run method.  This method will take the room config, logger factory, port and cancellation token
        //       Good practice would be to handle exceptions, one of which is a normal shutdown
        // Ref:  https://learn.microsoft.com/en-us/aspnet/core/fundamentals/?view=aspnetcore-10.0
        // Add your work below this line

        // Lab 0.2 - End

        // REF: Lab 0.2 - Start the non-Crestron web host on the configured port
        _ = Task.Run(async () =>
        {
            try
            {
                await WebHost.RunAsync(roomConfiguration, loggerFactory, settings.ApiPort, cancellationToken, mcpHosting);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                ErrorLog.Notice("Normal Shutdown");
                // Normal shutdown.
            }
            catch (Exception exception)
            {
                ErrorLog.Error("Web host failed: {0}", exception);
            }
        }, cancellationToken);
        // REF: Lab 0.2 - End

        ErrorLog.Notice($"Control System Initialized (API port {settings.ApiPort})");
    }

    // What the web host needs from the MCP room: a troubleshooting session per conversation (only with an API key), and a
    // callback, once its services exist, that registers the tools and connects to the gateway.
    private McpHosting CreateMcpHosting(McpRoom mcpRoom, McpSettings mcp, string roomName)
    {
        Func<ITroubleshootingSession>? createSession = mcp.IsApiKeyConfigured
            ? () => new TroubleshootingSessionAdapter(mcpRoom.CreateTroubleshootingSession(mcp.AnthropicApiKey!))
            : null;

        return new McpHosting(createSession, services => StartMcp(mcpRoom, services, roomName));
    }

    // Registers the room's MCP tools against the web host's RoomTools, reports the gateway registration state to the panels,
    // and connects to the gateway. In the earlier labs' versions RoomTools is not registered yet, so MCP is skipped.
    private void StartMcp(McpRoom mcpRoom, IServiceProvider services, string roomName)
    {
        var tools = services.GetService<RoomTools>();

        if (tools is null)
        {
            ErrorLog.Warn("MCP tools not registered; MCP not started");
            return;
        }

        var logger = _loggerFactory!.CreateLogger("P709.Mcp");
        var status = services.GetService<McpStatus>();

        McpRegistration.Register(mcpRoom, tools, logger);

        mcpRoom.RegistrationStateChanged += state =>
        {
            logger.LogInformation("MCP registration: {State}", state);
            status?.Set(state.ToString());
        };

        _ = Task.Run(async () =>
        {
            try
            {
                await mcpRoom.StartAsync(roomName);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "MCP could not connect to the gateway: {Reason}", exception.Message);
            }
        });
    }

    // A (re)connecting panel always gets the current connection details.
    private void OnPanelOnlineStatusChange(GenericBase currentDevice, OnlineOfflineEventArgs args)
    {
        // TODO: Lab 1.1d - Publish the connection details whenever the panel comes online
        // Hint: Use the [ConnectionDetailsPublisher].Publish method and perhaps log a warning if it returns false.
        // Add your work below this line

        // Lab 1.1d - End

        // REF: Lab 1.1d - Publish the connection details whenever the panel comes online
        if (args.DeviceOnLine && _publisher is not null && !_publisher.Publish())
        {
            ErrorLog.Warn("No host address available to publish SignalR connection details");
        }
        // REF: Lab 1.1d - End
    }

    private void OnProgramStatusEvent(eProgramStatusEventType programEventType)
    {
        if (programEventType != eProgramStatusEventType.Stopping)
        {
            return;
        }

        try
        {
            // Deregisters the room from the MCP Gateway; don't hold up the program stopping for long.
            _mcpRoom?.DisposeAsync().AsTask().Wait(2000);
        }
        catch (Exception exception)
        {
            ErrorLog.Error("MCP shutdown failed: {0}", exception.Message);
        }

        _shutdown.Cancel();
        _loggerFactory?.Dispose();
        _shutdown.Dispose();
    }
}
