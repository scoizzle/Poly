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
        var surface = McpTestTools.Surface;
        var sessionId = surface.Open("PackShared");

        var applied = surface.Define(sessionId, """
            domain D
            E: entity {
              Due: DateTime
              P: policy { Due < Now }
            }
            """);
        await Assert.That(applied.Success).IsTrue();

        var exported = surface.Export(sessionId, "dsl");
        await Assert.That(exported.Success).IsTrue();
        var poly = exported.Content;
        await Assert.That(poly).IsNotNull();
        await Assert.That(poly!.Contains("uses temporal", StringComparison.Ordinal)).IsFalse();
        await Assert.That(poly!).DoesNotContain("uses storage");
        await Assert.That(poly!).DoesNotContain("uses persistence");
        await Assert.That(poly!).Contains("Due < Now");
    }
}