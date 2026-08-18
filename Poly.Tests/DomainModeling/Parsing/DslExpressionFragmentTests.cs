using Poly.DomainModeling.Ontology;



namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>mcp-minify-1 parity: standalone DSL expression fragment API fail-closed cases.</summary>
public class DslExpressionFragmentTests {
    private static bool IsMagic(DslToken token) =>
        token.Kind == DslTokenKind.Identifier
        && string.Equals(token.Text, "MAGIC", StringComparison.Ordinal);

    [Test]
    public async Task Fragment_AgeGte18_IsComparison() {
        var expr = DslExpressionFragment.ParseExpressionFragment("Age >= 18");

        await Assert.That(expr).IsTypeOf<Comparison>();
        var cmp = (Comparison)expr;
        await Assert.That(cmp.Kind).IsEqualTo(ComparisonKind.GreaterThanOrEqual);
        await Assert.That(cmp.Left).IsTypeOf<PropertyAccess>();
        await Assert.That(((PropertyAccess)cmp.Left).Name).IsEqualTo("Age");
        await Assert.That(cmp.Right).IsTypeOf<Literal>();
        await Assert.That(((Literal)cmp.Right).Value).IsEqualTo(18L);
    }

    [Test]
    public async Task Fragment_AndOr_Parses() {
        var expr = DslExpressionFragment.ParseExpressionFragment("(A == 1) and (B == 2)");

        await Assert.That(expr).IsTypeOf<Poly.DomainModeling.Ontology.And>();
        var and = (Poly.DomainModeling.Ontology.And)expr;
        await Assert.That(and.Left).IsTypeOf<Comparison>();
        await Assert.That(and.Right).IsTypeOf<Comparison>();
    }

    [Test]
    public async Task Fragment_Empty_Throws() {
        await Assert.That(() => DslExpressionFragment.ParseExpressionFragment(""))
            .Throws<FormatException>();
        await Assert.That(() => DslExpressionFragment.ParseExpressionFragment("   "))
            .Throws<FormatException>();
    }

    [Test]
    public async Task Fragment_TrailingJunk_Throws() {
        var ex = await Assert.That(
            () => DslExpressionFragment.ParseExpressionFragment("Age >= 18 leftover"))
            .Throws<FormatException>();
        await Assert.That(ex!.Message).Contains("Trailing");
    }

    [Test]
    public async Task Fragment_OpenForm_Registry_Honored() {
        var builder = SessionBuilder.CreateEmpty();
        foreach (var rule in new[] { "expr-primary", "expr-primary-no-not" })
            builder.ExpressionForms.RegisterFold(rule, "magic", _ => DomainExpression.Literal(42L));
        builder.ExpressionForms.RegisterGrammarContributor(g => {
            foreach (var rule in new[] { "expr-primary", "expr-primary-no-not" }) {
                g.Define(rule)
                    .Pattern("magic", priority: 1)
                    .Predicate(IsMagic, "magic")
                    .Commit();
            }
        });
        var inputs = builder.Build();

        var expr = DslExpressionFragment.ParseExpressionFragment("MAGIC == 42", inputs);

        await Assert.That(expr).IsTypeOf<Comparison>();
        var cmp = (Comparison)expr;
        await Assert.That(cmp.Left).IsTypeOf<Literal>();
        await Assert.That(((Literal)cmp.Left).Value).IsEqualTo(42L);
    }
}