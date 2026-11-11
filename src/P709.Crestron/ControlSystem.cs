using Crestron.SimplSharp;
using Crestron.SimplSharpPro;
using Crestron.SimplSharpPro.UI;
using CrestronAgentLib.SimplSharpPro;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using P709.Core.Configuration;
using P709.Core.Connection;
using P709.Web;
using CrestronDirectory = Crestron.SimplSharp.CrestronIO.Directory;
using CrestronThread = Crestron.SimplSharpPro.CrestronThread.Thread;

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

        // TODO: Lab 0.2 - Start the non-Crestron web host on the configured port
        // Hint: The control system should spawn a new long-running task.  This will call our Web project
        //       static WebHost run method.  This method will take the room config, logger factory, port and cancellation token
        //       Good practice would be to handle exceptions, one of which is a normal shutdown
        // Ref:  https://learn.microsoft.com/en-us/aspnet/core/fundamentals/?view=aspnetcore-10.0
        // Add your work below this line

        // Lab 0.2 - End

        ErrorLog.Notice($"Control System Initialized (API port {settings.ApiPort})");
    }

    // A (re)connecting panel always gets the current connection details.
    private void OnPanelOnlineStatusChange(GenericBase currentDevice, OnlineOfflineEventArgs args)
    {
        // TODO: Lab 1.1d - Publish the connection details whenever the panel comes online
        // Hint: Use the [ConnectionDetailsPublisher].Publish method and perhaps log a warning if it returns false.
        // Add your work below this line

        // Lab 1.1d - End
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
