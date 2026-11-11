using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Poly.Mcp.Sessions;
using Poly.Mcp.Surface;

namespace Poly.Mcp;

/// <summary>
/// The one MCP host entry. <c>polyc mcp</c> calls it, and so does the <c>Poly.Mcp</c> forwarder
/// that keeps the old launch path working for a release. Stdio transport, logs to stderr, the two
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
                .AddCallToolFilter(UnknownArgumentFilter.Filter)
                // Registered last = innermost: it sees the tool result before the outer filters.
                .AddCallToolFilter(EnvelopeTextFilter.Filter));

        // v2 is the only surface: seven outcome tools. The old operation adapters remain
        // internal to PolySurfaceTools and are not registered as MCP tools.
        mcp.WithTools<PolySurfaceTools>();

        await builder.Build().RunAsync(cancel);
    }
}