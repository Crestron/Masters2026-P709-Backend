using Microsoft.AspNetCore.Http.Connections;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using P709.Core.Configuration;
using P709.Core.Nvx;
using P709.Web.Hubs;
using P709.Web.Infrastructure;
using P709.Web.Mcp;
using P709.Web.Nvx;

namespace P709.Web;

/// <summary>Builds and runs the lab's web server so it can be hosted by the Crestron program, a console app or a test.</summary>
public static class WebHost
{
    /// <summary>Runs the web server until <paramref name="cancellationToken"/> is cancelled.</summary>
    /// <param name="roomConfiguration">The room configuration the hub returns from GetRoomConfiguration.</param>
    /// <param name="loggerFactory">Optional caller-owned logger factory that receives all web host logging.</param>
    /// <param name="port">Port to listen on, from <c>LabSettings.ApiPort</c>. Never hard-code it: it must also match the port published to the panel. Null leaves Kestrel on its default.</param>
    /// <param name="cancellationToken">Stops the server.</param>
    /// <param name="mcp">Optional MCP hook-up from the program (Lab 4): the troubleshooting session factory, and a callback given the services once they exist.</param>
    public static async Task RunAsync(RoomConfiguration roomConfiguration, ILoggerFactory? loggerFactory = null, int? port = null, CancellationToken cancellationToken = default, McpHosting? mcp = null)
    {
        var app = Build(roomConfiguration, loggerFactory, port, mcp: mcp);
        NotifyServicesReady(app, mcp);
        await app.RunAsync(cancellationToken);
    }

    // Hands the program the services so it can register its MCP tools. A failure there must not stop the room's web host.
    private static void NotifyServicesReady(WebApplication app, McpHosting? mcp)
    {
        if (mcp?.OnServicesReady is not { } onServicesReady)
        {
            return;
        }

        try
        {
            onServicesReady(app.Services);
        }
        catch (Exception exception)
        {
            app.Logger.LogError(exception, "MCP could not be started: {Reason}", exception.Message);
        }
    }

    /// <summary>Log category for the NVX going offline and coming back.</summary>
    public const string NvxConnectivityLogCategory = "P709.Web.Nvx.NvxConnectivity";

    // Logs the NVX receiver going offline (a warning: the Crestron error log) and coming back, once each. Every request made
    // while it is offline fails at once and is logged only at debug level, so an offline NVX does not fill the log.
    private static void LogNvxConnectivity(WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(NvxConnectivityLogCategory);

        app.Services.GetRequiredService<NvxRouter>().ConnectivityChanged += change =>
        {
            if (change.IsOnline)
            {
                logger.LogInformation("The NVX at {Address} is back online.", change.Address);
            }
            else
            {
                logger.LogWarning("The NVX at {Address} is offline: {Reason} Sources still switch; the NVX is retried every {Seconds:0} seconds.",
                    change.Address, change.Reason, NvxConnectivityGuard.RetryInterval.TotalSeconds);
            }
        };
    }

    /// <summary>Builds the web application without starting it.</summary>
    /// <param name="roomConfiguration">The room configuration the hub returns from GetRoomConfiguration.</param>
    /// <param name="loggerFactory">Optional caller-owned logger factory that receives all web host logging.</param>
    /// <param name="port">Port to listen on. Null leaves Kestrel on its default.</param>
    /// <param name="nvxReceiver">The client for the NVX receiver. Null makes a real one from the room's <c>nvx</c> section; tests pass a fake.</param>
    /// <param name="mcp">Optional MCP hook-up (Lab 4). Without it, troubleshooting on the MCP hub reports itself unavailable.</param>
    public static WebApplication Build(RoomConfiguration roomConfiguration, ILoggerFactory? loggerFactory = null, int? port = null, INvxClient? nvxReceiver = null, McpHosting? mcp = null)
    {
        // Embedded hosts have an unrelated working directory and their own process args.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            Args = []
        });

        if (port is not null)
        {
            // All interfaces, so the touch panel's browser can reach the hub.
            builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
        }

        if (loggerFactory is not null)
        {
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(new ForwardingLoggerProvider(loggerFactory));
        }

        ArgumentNullException.ThrowIfNull(roomConfiguration);

        // TODO: Lab 1.1c - Register the room configuration and state as singletons
        // Hint: We will add singletons to the web application builder services
        //       The room configuration is an instance that has been passed into the method.
        //       The room state will be added using lazy instantiation - using the generic templated form of adding a singleton.
        // Ref:  https://learn.microsoft.com/en-us/aspnet/core/fundamentals/?view=aspnetcore-10.0
        // Add your work below this line

        // Lab 1.1c - End

        // REF: Lab 1.1c - Register the room configuration and state as singletons
        builder.Services.AddSingleton(roomConfiguration);
        builder.Services.AddSingleton<RoomState>();
        // REF: Lab 1.1c - End

        // TODO: Lab 2.5a - Register the NVX router as a singleton
        // Hint: When the room has an nvx section, make an NvxRestClient for its ReceiverAddress with its Username and Password
        //       (unless the tests passed in nvxReceiver). Then register one NvxRouter made from roomConfiguration.Nvx and that
        //       client, so the hub and the preview endpoint share it, and its NVX session, for the life of the program.
        // Add your work below this line

        // Lab 2.5a - End

        // REF: Lab 2.5a - Register the NVX router as a singleton
        var nvx = roomConfiguration.Nvx;
        var receiver = nvxReceiver ?? (nvx is null ? null : new NvxRestClient(nvx.ReceiverAddress, nvx.Username, nvx.Password));
        builder.Services.AddSingleton(new NvxRouter(nvx, receiver));
        // REF: Lab 2.5a - End

        // MCP plumbing: the troubleshooting conversation and the gateway registration status, shared by every MCP hub client.
        // They need nothing from the earlier labs, so registering them is harmless whether or not the MCP hub is mapped.
        builder.Services.AddSingleton<McpStatus>();
        builder.Services.AddSingleton(services => new TroubleshootingService(
            mcp?.CreateTroubleshootingSession,
            services.GetRequiredService<ILogger<TroubleshootingService>>()));

        // TODO: Lab 4.2a - Register the MCP room tools, and the service that pushes MCP changes to the panels
        // Hint: RoomTools is what every MCP tool calls. Everything it needs is already registered, so add the type itself as a
        //       singleton, as was done for the room state in Lab 1.1c. McpHubBroadcaster sends troubleshooting and status changes
        //       to the panels for the life of the web host: add it as a hosted service.
        // Ref:  https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0
        // Add your work below this line

        // Lab 4.2a - End

        // REF: Lab 4.2a - Register the MCP room tools, and the service that pushes MCP changes to the panels
        builder.Services.AddSingleton<RoomTools>();
        builder.Services.AddHostedService<McpHubBroadcaster>();
        // REF: Lab 4.2a - End

        // TODO: Lab 1.1a - Register SignalR, using Newtonsoft.Json for the hub payloads
        // Hint: We want to add the SignalR service to the web app builder.
        //       The returned signalR builder should then be configured to support the Newtonsoft Json Protocol
        //       This handler should specify the payload serialiser will use camelCase property names and will
        //       convert enums to their string representation.
        // Ref:  https://learn.microsoft.com/en-us/aspnet/core/signalr/introduction?view=aspnetcore-10.0
        // Add your work below this line

        // Lab 1.1a - End

        // REF: Lab 1.1a - Register SignalR, using Newtonsoft.Json for the hub payloads
        // Newtonsoft.Json is the chosen serialiser, so hub payloads use it too: camelCase names and enums as their
        // EnumMember strings, which is what the Svelte client expects.
        var signalR = builder.Services.AddSignalR(options =>
        {
            // Send exception details to the client. Useful in a lab; turn off in production.
            options.EnableDetailedErrors = true;
        });

        signalR.AddNewtonsoftJsonProtocol(options =>
        {
            var json = options.PayloadSerializerSettings;
            json.ContractResolver = new CamelCasePropertyNamesContractResolver(); // roomName, not RoomName
            json.Converters.Add(new StringEnumConverter());                       // "on", not 1
        });
        // REF: Lab 1.1a - End

        builder.Services.AddHealthChecks();
        builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
            .SetIsOriginAllowed(_ => true)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()));

        var app = builder.Build();

        LogNvxConnectivity(app);

        app.UseCors();
        // TODO: Lab 1.1b - Map the SignalR hub at its path
        // Hint: Finally, we need to tell app to map the SignalR hub using the controller implementation and
        //       specify its path (also defined in the controller implementation).
        // Add your work below this line

        // Lab 1.1b - End

        // REF: Lab 1.1b - Map the SignalR hub at its path
        app.MapHub<RoomHub>(RoomHub.Path);

        // You could force a particular transport if you wish
        // app.MapHub<RoomHub>(RoomHub.Path, options =>
        // {
        //    options.Transports = HttpTransportType.WebSockets | HttpTransportType.ServerSentEvents;
        // });
        // REF: Lab 1.1b - End

        // TODO: Lab 2.5c - Map the NVX preview endpoint
        // Hint: NvxPreviewEndpoint has an extension method that maps GET /nvx/preview/{sourceId} on the app.
        // Add your work below this line

        // Lab 2.5c - End

        // REF: Lab 2.5c - Map the NVX preview endpoint
        app.MapNvxPreview();
        // REF: Lab 2.5c - End

        // TODO: Lab 4.2b - Map the MCP hub at the room configuration's mcpHubPath
        // Hint: Like Lab 1.1b, but for McpHub, and at the path the room configuration gives (the panel reads the same path from
        //       GetRoomConfiguration). The path is optional: map the hub only when the room has one.
        // Add your work below this line

        // Lab 4.2b - End

        // REF: Lab 4.2b - Map the MCP hub at the room configuration's mcpHubPath
        if (roomConfiguration.McpHubPath is { } mcpHubPath)
        {
            app.MapHub<McpHub>(mcpHubPath);
        }
        // REF: Lab 4.2b - End

        // We haven't registered any health checks, but the default /health endpoint is
        // still useful to see if the server is running.
        app.MapHealthChecks("/health");

        return app;
    }
}
