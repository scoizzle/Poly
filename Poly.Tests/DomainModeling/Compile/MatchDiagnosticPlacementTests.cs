using Poly.Compile;
using Poly.Packs.Product;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// #508: a <c>match</c> diagnostic carries one position — the <c>match</c> keyword the analysis recorded, not
/// the nearest named element that holds it — and its message does not repeat that position.
/// </summary>
public class MatchDiagnosticPlacementTests {
    private const string MismatchedArms = """
        domain P
        O: entity {
          Qty: Number
          Tier: Text
          Go: action { assign Tier to match { Qty > 1 -> "a", else -> 3 } }
        }
        """;

    private const string MissingElse = """
        domain P
        O: entity {
          Qty: Number
          Tier: Text
          Go: action { assign Tier to match { Qty > 1 -> "a" } }
        }
        """;

    private static int LineOf(string text, string needle) =>
        Array.FindIndex(text.Split('\n'), line => line.Contains(needle, StringComparison.Ordinal)) + 1;

    private static int ColumnOf(string text, string lineNeedle, string needle) {
        var line = text.Split('\n').Single(l => l.Contains(lineNeedle, StringComparison.Ordinal));
        return line.IndexOf(needle, StringComparison.Ordinal) + 1;
    }

    private static CompileResult Compile(string text) =>
        new PolyCompiler(ProductCatalog.Catalog, new SessionOptions(ProductCatalog.Catalog, ProductCatalog.ImplicitIds))
            .Compile(text, "match.poly");

    [Test]
    public async Task Match_ArmMismatch_IsPlacedAtTheMatchKeyword() {
        var error = Compile(MismatchedArms).Diagnostics.Single(d => d.Code == "DMMAT002");

        await Assert.That(error.Placed).IsTrue();
        await Assert.That(error.Line).IsEqualTo(LineOf(MismatchedArms, "match {"));
        await Assert.That(error.Column).IsEqualTo(ColumnOf(MismatchedArms, "match {", "match"));
        await Assert.That(error.Message).DoesNotContain("(line ");
        await Assert.That(error.Message).Contains("'Text' and 'Number'");
    }

    [Test]
    public async Task Match_MissingElse_IsOnePositionAtTheMatchKeyword() {
        var error = Compile(MissingElse).Diagnostics.Single(d => d.IsError);

        await Assert.That(error.Code).IsEqualTo("DSL0001");
        await Assert.That(error.Line).IsEqualTo(LineOf(MissingElse, "match {"));
        await Assert.That(error.Column).IsEqualTo(ColumnOf(MissingElse, "match {", "match"));
        await Assert.That(error.Message).DoesNotContain("(line ");
        await Assert.That(error.Message).Contains("'match' requires a final 'else' arm");
    }
}