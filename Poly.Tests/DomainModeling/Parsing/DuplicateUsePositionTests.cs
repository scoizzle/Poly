using Poly.Compile;
using Poly.DomainModeling.Language;
using Poly.Grammar;
using Poly.Packs.Product;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// #872: a duplicate <c>uses</c> id is one positioned refusal from the header reader, at the second
/// id — the same refusal every door sees. The compiler keeps its <c>DSL0003</c> code for it.
/// </summary>
public class DuplicateUsePositionTests {
    private const string Duplicate = """
        domain A
        uses http
        uses http
        E: entity {
          N: Text
        }
        """;

    [Test]
    public async Task HeaderReader_ReportsTheDuplicate_AtTheSecondId() {
        var ex = Assert.Throws<GrammarException>(() => DslSyntaxTree.Parse(Duplicate))!;

        await Assert.That(ex.Detail).IsEqualTo("Domain lists extension 'http' more than once.");
        await Assert.That(ex.Line).IsEqualTo(3);
        await Assert.That(ex.Column).IsEqualTo(6);
    }

    [Test]
    public async Task Compiler_KeepsTheDuplicateExtensionCode_AtTheSamePosition() {
        var compiler = new PolyCompiler(
            ProductCatalog.Catalog,
            new SessionOptions(ProductCatalog.Catalog, ExtensionCatalog.ProductAuthoring));

        var refused = compiler.Analyze(Duplicate, "duplicate.poly").Diagnostics.Single(d => d.IsError);

        await Assert.That(refused.Code).IsEqualTo(DiagnosticCodes.DuplicateExtension);
        await Assert.That((refused.Line, refused.Column)).IsEqualTo((3, 6));
        await Assert.That(refused.Message).IsEqualTo("Domain lists extension 'http' more than once.");
    }
}