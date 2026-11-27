using Poly.Grammar;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// Quantifier and aggregate spellings the guide does not ship — <c>first Rel where …</c>, <c>count filter Rel where …</c>,
/// <c>sum Rel (expr)</c> — are refused with a positioned diagnostic that names the unsupported form and the supported
/// spelling, not the "Expected RBrace" the expression boundary produced (#505).
/// </summary>
public sealed class UnsupportedAggregateFormTests {
    private const string Domain = "domain P\nLine: entity {\n  Qty: Number range(0, 100)\n}\nO: entity {\n  lines: many Line\n  F: policy { ";

    private static GrammarException PolicyError(string expression) {
        var poly = Domain + expression + " }\n}\n";
        try {
            new PolyDslParser(poly).Parse();
        }
        catch (GrammarException ex) {
            return ex;
        }

        throw new InvalidOperationException($"Expected a parse error for '{expression}'.");
    }

    private static int ColumnOf(string expression, string word) => expression.IndexOf(word, StringComparison.Ordinal) + 1;

    [Test]
    public async Task First_WithWhere_NamesTheUnsupportedQuantifierAtItsWord() {
        const string expression = "first lines where Qty > 1";
        var error = PolicyError(expression);

        await Assert.That(error.Detail).Contains("'first' is not supported");
        await Assert.That(error.Detail).Contains("'where'");
        await Assert.That(error.Line).IsEqualTo(7);
        await Assert.That(error.Column).IsEqualTo(ColumnOf("  F: policy { " + expression, "first"));
    }

    [Test]
    public async Task CountFilter_NamesFilterAtItsWord() {
        const string expression = "count filter lines where Qty > 1 > 0";
        var error = PolicyError(expression);

        await Assert.That(error.Detail).Contains("'filter' is not supported");
        await Assert.That(error.Detail).Contains("where");
        await Assert.That(error.Line).IsEqualTo(7);
        await Assert.That(error.Column).IsEqualTo(ColumnOf("  F: policy { " + expression, "filter"));
    }

    [Test]
    public async Task Sum_WithAnExpression_NamesTheSupportedSumFormAtItsWord() {
        const string expression = "sum lines (Qty * 2) > 5";
        var error = PolicyError(expression);

        await Assert.That(error.Detail).Contains("'sum'");
        await Assert.That(error.Detail).Contains("sum Rel Prop");
        await Assert.That(error.Line).IsEqualTo(7);
        await Assert.That(error.Column).IsEqualTo(ColumnOf("  F: policy { " + expression, "sum"));
    }

    [Test]
    public async Task Sum_WithoutARelationship_NamesTheSupportedSumFormAtItsWord() {
        const string expression = "sum (Qty * 2) > 5";
        var error = PolicyError(expression);

        await Assert.That(error.Detail).Contains("sum Rel Prop");
        await Assert.That(error.Line).IsEqualTo(7);
        await Assert.That(error.Column).IsEqualTo(ColumnOf("  F: policy { " + expression, "sum"));
    }

    [Test]
    public async Task SupportedForms_StillParse() {
        var changes = new PolyDslParser("""
            domain P
            Line: entity { Qty: Number range(0, 100) }
            O: entity {
              lines: many Line
              AnyLine: policy { any lines where Qty > 1 }
              LineCount: policy { count lines > 2 }
              LineTotal: policy { sum lines Qty > 5 }
            }
            """).Parse();

        await Assert.That(changes).IsNotEmpty();
    }
}