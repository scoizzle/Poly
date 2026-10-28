using Poly.Mcp.Sessions;
using Poly.Mcp.Tools;

namespace Poly.Tests.Mcp;

/// <summary>
/// apply_dsl + export_dsl resolve language from the domain's extension list
/// (seeded when the source omits <c>uses</c>).
/// </summary>
public sealed class McpPackSharedInputsTests {
    [Test]
    public async Task ApplyAndExportDsl_OmitsImplicitTemporal() {
        var (sessionId, _) = McpTestTools.Store.Create("PackShared");

        var applied = McpTestTools.Dsl.ApplyDsl(sessionId, """
            domain D
            E: entity {
              Due: DateTime
              P: policy { Due < Now }
            }
            """);
        await Assert.That(applied.Success).IsTrue();

        var exported = McpTestTools.Dsl.ExportDsl(sessionId);
        await Assert.That(exported.Success).IsTrue();
        var poly = exported.Data!.GetType().GetProperty("poly")?.GetValue(exported.Data) as string;
        await Assert.That(poly).IsNotNull();
        await Assert.That(poly!.Contains("uses temporal", StringComparison.Ordinal)).IsFalse();
        await Assert.That(poly!).Contains("uses storage");
        await Assert.That(poly!).Contains("Due < Now");
    }
}