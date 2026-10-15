using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Parsing;

public sealed class SumParseTests {
    private static DomainExpression ParsePolicyExpression(string expression) {
        var poly = $$"""
            domain Test

            Line: entity {
              Price: Number
              P: policy { {{expression}} }
            }
            """;
        var changes = new PolyDslParser(poly).Parse();
        return changes.OfType<AddPolicyToEntityChange>().First().Policy.Expression;
    }

    [Test]
    public async Task Sum_RelProp_Parses() {
        var expr = ParsePolicyExpression("sum lines Price");
        await Assert.That(expr).IsTypeOf<SumExpr>();
        var sum = (SumExpr)expr;
        await Assert.That(sum.RelationshipName).IsEqualTo("lines");
        await Assert.That(sum.PropertyName).IsEqualTo("Price");
    }

    [Test]
    public async Task Sum_RelProp_Prints() {
        var expr = ParsePolicyExpression("sum lines Price");
        await Assert.That(new DomainDslPrinter().PrintTestExpression(expr)).IsEqualTo("sum lines Price");
    }

    [Test]
    public async Task Sum_Where_Rejected() {
        await Assert.That(() => ParsePolicyExpression("sum lines where Open"))
            .Throws<FormatException>()
            .WithMessageContaining("sum does not take where");
    }

    [Test]
    public async Task Count_Bare_Unchanged() {
        var expr = ParsePolicyExpression("count lines");
        await Assert.That(expr).IsTypeOf<CountExpr>();
        var count = (CountExpr)expr;
        await Assert.That(count.RelationshipName).IsEqualTo("lines");
        await Assert.That(count.Body).IsNull();
    }

    [Test]
    public async Task Sum_MulParen_Rejected() {
        await Assert.That(() => ParsePolicyExpression("sum lines (Price * Qty)"))
            .Throws<FormatException>();
    }
}