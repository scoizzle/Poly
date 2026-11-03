using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Runtime;
using Poly.Mcp.Tools;

namespace Poly.Tests.Mcp;

/// <summary>
/// S13 (#368): the MCP runtime executes the compiled module. The stored instance is
/// the dictionary the cached static <c>Create</c> built, not a <see cref="DomainEntityInstance"/>.
/// </summary>
public class McpModuleRuntimeTests {
    [Test]
    public async Task McpCreate_ExecutesCachedModuleCreate() {
        var (sessionId, _) = McpTestTools.Store.Create("ModuleRuntime");
        await Assert.That(McpTestTools.Dsl.ApplyDsl(sessionId, """
            domain Ledger
            Entry: entity { Memo: Text }
            """).Success).IsTrue();

        var create = McpTestTools.Runtime.CreateInstance(sessionId, "Entry", """{"Memo":"first"}""");
        await Assert.That(create.Success).IsTrue();

        await Assert.That(McpTestTools.Store.TryGet(sessionId, out var state)).IsTrue();
        await Assert.That(RuntimeAnalysisCache.TryGetModuleMethod(
            state.Domain, "Entry", "Create", out var createMethod)).IsTrue();
        await Assert.That(createMethod!.IsStatic).IsTrue();

        object stored = state.InstanceMap.Values.Single();
        await Assert.That(stored is DomainEntityInstance).IsFalse();
        await Assert.That(stored is IDictionary<string, object?>).IsTrue();
        await Assert.That(((IDictionary<string, object?>)stored)["Memo"]).IsEqualTo("first");
    }
}