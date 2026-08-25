using Poly.Mcp.Sessions;
using Poly.Mcp.Tools;

namespace Poly.Tests.Mcp;

/// <summary>
/// apply_dsl + export_dsl resolve language from the domain's extension list
/// (seeded when the source omits <c>uses</c>).
/// </summary>
public sealed class McpPackSharedInputsTests {
    [Test]
    public async Task ApplyAndExportDsl_RoundTripDomainExtensions() {
        var (sessionId, _) = McpSessionStore.Create("PackShared");

        var applied = DslTool.ApplyDsl(sessionId, """
            domain D
            E: entity {
              Due: Date
              P: policy { Due < Now }
            }
            """);
        await Assert.That(applied.Success).IsTrue();

        var exported = DslTool.ExportDsl(sessionId);
        await Assert.That(exported.Success).IsTrue();
        var poly = exported.Data!.GetType().GetProperty("poly")?.GetValue(exported.Data) as string;
        await Assert.That(poly).IsNotNull();
        await Assert.That(poly!).Contains("uses temporal");
        await Assert.That(poly!).Contains("Due < Now");
    }
}