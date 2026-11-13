using Poly.DomainModeling.Libraries.Temporal;
using Poly.DomainModeling.Ontology;

using Subtract = Poly.DomainModeling.Ontology.Subtract;



namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>mcp-minify-1 parity: standalone DSL expression fragment API fail-closed cases.</summary>
public class DslExpressionFragmentTests {
    private static bool IsMagic(DslToken token) =>
        token.Kind == DslTokenKind.Identifier
        && token.TextEquals("MAGIC");

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
            () => DslExpressionFragment.ParseExpressionFragment("Age >= 18 )"))
            .Throws<FormatException>();
        await Assert.That(ex!.Message).Contains("Trailing");
    }

    [Test]
    public async Task Fragment_Parse_NowMinusDuration_AppliesNoLibraryMeaning() {
        var expr = DslExpressionFragment.ParseExpressionFragment("Now - 12 Days");

        await Assert.That(expr).IsNotTypeOf<DateOperation>();
        await Assert.That(expr).IsTypeOf<Subtract>();
        var sub = (Subtract)expr;
        await Assert.That(sub.Left).IsTypeOf<PropertyAccess>();
        await Assert.That(((PropertyAccess)sub.Left).Name).IsEqualTo("Now");
        await Assert.That(sub.Right).IsTypeOf<UnresolvedDuration>();
        var duration = (UnresolvedDuration)sub.Right;
        await Assert.That(duration.Amount).IsEqualTo(12L);
        await Assert.That(duration.Unit).IsEqualTo("Days");
    }

    [Test]
    public async Task Fragment_Resolve_WithTemporal_IsDateOperation() {
        var expr = DslExpressionFragment.Resolve(
            DslExpressionFragment.ParseExpressionFragment("Now - 12 Days"),
            ExtensionCatalog.Core.Language);

        await Assert.That(expr).IsTypeOf<DateOperation>();
        var dateOp = (DateOperation)expr;
        await Assert.That(dateOp.Kind).IsEqualTo(DateOperationKind.AddDays);
        await Assert.That(((Literal)dateOp.Offset).Value).IsEqualTo(-12L);
        await Assert.That(dateOp.Date).IsTypeOf<Now>();
    }

    [Test]
    public async Task Fragment_Resolve_WithoutTemporal_Throws() {
        var raw = SessionBuilder.CreateEmpty().LoadCore().Build();
        await Assert.That(() => DslExpressionFragment.Resolve(
                DslExpressionFragment.ParseExpressionFragment("Now - 12 Days"), raw))
            .Throws<FormatException>()
            .WithMessageContaining("requires uses temporal");
    }

    [Test]
    public async Task Fragment_OpenForm_Registry_Honored() {
        var builder = SessionBuilder.CreateEmpty();
        foreach (var rule in new[] { "expr-primary" })
            builder.ExpressionForms.RegisterFold(rule, "magic", _ => DomainExpression.Literal(42L));
        builder.ExpressionForms.RegisterGrammarContributor(g => {
            foreach (var rule in new[] { "expr-primary" }) {
                g.Define(rule)
                    .Pattern("magic", priority: 1)
                    .Predicate(IsMagic, "magic")
                    .Commit();
            }
        });
        var inputs = builder.Build();

        var expr = DslExpressionFragment.ParseExpressionFragment("MAGIC == 42");

        await Assert.That(expr).IsTypeOf<Comparison>();
        var cmp = (Comparison)expr;
        await Assert.That(cmp.Left).IsTypeOf<PropertyAccess>();
        await Assert.That(((PropertyAccess)cmp.Left).Name).IsEqualTo("MAGIC");
        var resolved = DslExpressionFragment.Resolve(expr, inputs);
        await Assert.That(((Comparison)resolved).Left).IsTypeOf<PropertyAccess>();
    }
}