using Poly.Compile;
using Poly.Packs.Product;

namespace Poly.Tests.Polyc;

/// <summary>
/// #552: <c>Poly.Packs.MySql</c> ships, but the product catalog does not load it (no host
/// provider arm), so <c>uses mysql</c> is refused. The refusal names the pack and the reason
/// instead of reporting it as an unknown id, on both catalogs polyc and MCP share.
/// </summary>
public sealed class MySqlPackSelectionTests {
    private const string Doc = "domain L\nuses mysql\n\nJob: entity {\n  N: Number\n}\n";

    private static PolyCompiler CompilerOver(ExtensionCatalog catalog) =>
        new(catalog, new SessionOptions(catalog, ProductCatalog.ImplicitIds));

    private static CompileDiagnostic MySqlDiagnostic(PolyCompiler compiler) =>
        compiler.Compile(Doc, "mysql.poly").Diagnostics
            .Single(d => d.Code == DiagnosticCodes.UnknownExtension);

    [Test]
    public async Task UsesMySql_ProductCatalog_NamesThePackAndWhyItIsRefused() {
        var diagnostic = MySqlDiagnostic(CompilerOver(ProductCatalog.Catalog));

        await Assert.That(diagnostic.Message).Contains("Poly.Packs.MySql");
        await Assert.That(diagnostic.Message).Contains("not selectable");
        await Assert.That(diagnostic.Message).Contains("host provider");
        await Assert.That(diagnostic.Message).DoesNotContain("Unknown domain extension");
        await Assert.That(diagnostic.Placed).IsTrue();
    }

    /// <summary>
    /// The MCP harness simulates from <see cref="ProductCatalog.Model"/> and polyc from
    /// <see cref="ProductCatalog.Catalog"/>: one catalog constructor, so one refusal.
    /// </summary>
    [Test]
    public async Task UsesMySql_ModelCatalog_ReportsTheSameRefusal() {
        var product = MySqlDiagnostic(CompilerOver(ProductCatalog.Catalog));
        var model = MySqlDiagnostic(CompilerOver(ProductCatalog.Model));

        await Assert.That(model.Message).Contains("Domain extension 'mysql' is not selectable");
        await Assert.That(model.Message).IsEqualTo(product.Message);
    }

    /// <summary>The MCP define door reports the same refusal, unchanged.</summary>
    [Test]
    public async Task Define_UsesMySql_ReportsTheRefusal() {
        var (sessionId, _) = McpTestTools.Store.Create("My");
        var applied = McpTestTools.Surface.Define(sessionId, Doc);
        await Assert.That(applied.Success).IsFalse();
        await Assert.That(applied.Summary).Contains("Domain extension 'mysql' is not selectable");
        await Assert.That(applied.Summary).Contains("Use 'sqlite' or 'sqlserver'");
    }

    [Test]
    public async Task ExtensionError_CarriedId_IsNull() =>
        await Assert.That(ProductCatalog.Catalog.ExtensionError("sqlite")).IsNull();
}