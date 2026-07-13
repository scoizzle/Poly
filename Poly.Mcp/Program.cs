using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Poly.Mcp.Tools;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(consoleLogOptions => {
    // Configure all logs to go to stderr
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<V3SessionTool>()
    .WithTools<V3QueryTool>()
    .WithTools<V3EvolveTool>()
    .WithTools<V3PolicyTool>();

await builder.Build().RunAsync();