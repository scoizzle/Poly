using Poly.Compile;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.Packs.Product;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// #514: a property declared twice on one entity must report the positioned structural duplicate
/// (<see cref="DomainModelDiagnosticCodes.StructuralDuplicate"/>), not escape as an unplaced
/// <c>DSL0004</c> carrying the framework's dictionary message.
/// </summary>
public sealed class DuplicatePropertyNameTests {
    private static CompileResult Compile(string dsl) =>
        new PolyCompiler(ProductCatalog.Catalog, new SessionOptions(ProductCatalog.Catalog, ExtensionCatalog.ProductAuthoring))
            .Compile(dsl, "duplicate-property.poly");

    [Test]
    public async Task DuplicatePropertyName_ReportsPositionedStructuralDuplicate() {
        const string dsl = """
            domain T
            A: entity { N: Text N: Number }
            """;

        var result = Compile(dsl);

        var duplicates = result.Diagnostics
            .Where(d => d.Code == DomainModelDiagnosticCodes.StructuralDuplicate)
            .ToList();
        await Assert.That(duplicates.Count).IsEqualTo(1);
        await Assert.That(duplicates[0].Placed).IsTrue();
        await Assert.That(duplicates[0].Line).IsEqualTo(2);
        await Assert.That(duplicates[0].Message).Contains("Duplicate member name 'N' in entity 'A'.");
        await Assert.That(result.Diagnostics.Any(d => d.Code == "DSL0004")).IsFalse();
    }
}