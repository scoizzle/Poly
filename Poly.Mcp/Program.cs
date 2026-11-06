using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Poly.Mcp;
using Poly.Mcp.Sessions;
using Poly.Mcp.Surface;
using Poly.Mcp.Tools;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(consoleLogOptions => {
    // Configure all logs to go to stderr
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.Configure<McpSessionOptions>(builder.Configuration.GetSection("Mcp:Sessions"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ISessionStore, McpSessionStore>();
builder.Services.AddHostedService<SessionEvictionService>();

var surface = Environment.GetEnvironmentVariable("POLY_MCP_SURFACE");
var useV2 = !string.Equals(surface, "1", StringComparison.Ordinal);

var mcp = builder.Services
    .AddMcpServer(options => {
        // The instructions describe the surface that is actually registered; v1 mode must
        // not tell the client to call poly_* tools that do not exist.
        if (useV2)
            options.ServerInstructions = PolyMcpInstructions.Text;
    })
    .WithStdioServerTransport()
    .WithRequestFilters(filters => filters
        .AddCallToolFilter(ToolExceptionBoundary.Filter)
        .AddCallToolFilter(UnknownArgumentFilter.Filter));

if (useV2) {
    mcp.WithTools<Poly.Mcp.Surface.PolySurfaceTools>();
}
else {
    mcp.WithTools<SessionTool>()
        .WithTools<QueryTool>()
        .WithTools<EvolveTool>()
        .WithTools<PolicyTool>()
        .WithTools<DslTool>()
        .WithTools<OracleTool>()
        .WithTools<RuntimeTool>();
}

await builder.Build().RunAsync();