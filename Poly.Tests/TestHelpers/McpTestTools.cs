using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Poly.Mcp.Sessions;
using Poly.Mcp.Tools;

namespace Poly.Tests.TestHelpers;

/// <summary>
/// Test composition root for the MCP tools: one process-wide store and the tool
/// instances wired the way <c>Program.cs</c> wires them. Tests keep the previous
/// behavior of a single shared session store.
/// </summary>
internal static class McpTestTools {
    public static readonly McpSessionStore Store = new(
        TimeProvider.System,
        Options.Create(new McpSessionOptions()),
        NullLogger<McpSessionStore>.Instance);

    public static readonly SessionTool Session = new(Store, NullLogger<SessionTool>.Instance);
    public static readonly QueryTool Query = new(Store, NullLogger<QueryTool>.Instance);
    public static readonly EvolveTool Evolve = new(Store, NullLogger<EvolveTool>.Instance);
    public static readonly PolicyTool Policy = new(Store, NullLogger<PolicyTool>.Instance);
    public static readonly DslTool Dsl = new(Store, NullLogger<DslTool>.Instance);
    public static readonly OracleTool Oracle = new(Store, NullLogger<OracleTool>.Instance);
    public static readonly RuntimeTool Runtime = new(Store, NullLogger<RuntimeTool>.Instance);
}