using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Poly.Mcp;
using Poly.Mcp.Sessions;
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

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithRequestFilters(filters => filters.AddCallToolFilter(ToolExceptionBoundary.Filter))
    .WithTools<SessionTool>()
    .WithTools<QueryTool>()
    .WithTools<EvolveTool>()
    .WithTools<PolicyTool>()
    .WithTools<DslTool>()
    .WithTools<OracleTool>()
    .WithTools<RuntimeTool>();

await builder.Build().RunAsync();