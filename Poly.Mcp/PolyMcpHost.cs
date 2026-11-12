using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ModelContextProtocol.Server;

using Poly.Mcp.Sessions;
using Poly.Mcp.Surface;

namespace Poly.Mcp;

/// <summary>
/// The one MCP host entry. <c>polyc mcp</c> calls it. Stdio transport, logs to stderr, the two
/// call filters, and the seven outcome tools.
/// </summary>
public static class PolyMcpHost {
    public static async Task RunAsync(string[] args, CancellationToken cancel = default) {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Logging.AddConsole(consoleLogOptions => {
            // Configure all logs to go to stderr
            consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        builder.Services.Configure<McpSessionOptions>(builder.Configuration.GetSection("Mcp:Sessions"));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ISessionStore, McpSessionStore>();
        builder.Services.AddHostedService<SessionEvictionService>();

        var mcp = builder.Services
            .AddMcpServer(options => options.ServerInstructions = PolyMcpInstructions.Text)
            .WithStdioServerTransport()
            .WithRequestFilters(filters => filters
                .AddCallToolFilter(ToolExceptionBoundary.Filter)
                .AddCallToolFilter(ArgumentValidationFilter.Filter)
                // Registered last = innermost: it sees the tool result before the outer filters.
                .AddCallToolFilter(EnvelopeTextFilter.Filter));

        // v2 is the only surface: seven outcome tools. The old operation adapters remain
        // internal to PolySurfaceTools and are not registered as MCP tools.
        mcp.WithTools<PolySurfaceTools>();

        var app = builder.Build();

        // Startup self-check: every registered tool's published inputSchema must map, or the host
        // refuses to serve. An unmapped tool would let a caller error reach the binder untyped.
        ArgumentSpecCatalog.SelfCheck(
            app.Services.GetRequiredService<IOptions<McpServerOptions>>().Value.ToolCollection
            ?? throw new InvalidOperationException("The MCP host has no tool collection to validate."));

        await app.RunAsync(cancel);
    }
}