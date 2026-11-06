using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Poly.Mcp;
using Poly.Mcp.Sessions;
using Poly.Mcp.Surface;

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
        .AddCallToolFilter(UnknownArgumentFilter.Filter));

// v2 is the only surface: seven outcome tools. The old operation adapters remain
// internal to PolySurfaceTools and are not registered as MCP tools.
mcp.WithTools<PolySurfaceTools>();

await builder.Build().RunAsync();