using Poly.DomainModeling;
using Poly.DomainModeling.Parsing;
using Poly.Grammar;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>mcp-minify-1: standalone DSL expression fragment API fail-closed cases.</summary>
public class DslExpressionFragmentTests {
    /// <summary>Pack form: identifier <c>MAGIC</c> → literal 42 (same shape as E1 tests).</summary>
    private sealed class MagicLiteralForm : IExpressionPrimaryForm {
        public bool TryParse(IDslParseCursor cursor, DslExpressionParser expressions, out DomainExpression expression) {
            expression = null!;
            if (cursor.Current.Kind != DslTokenKind.Identifier
                || !string.Equals(cursor.Current.Text, "MAGIC", StringComparison.Ordinal))
                return false;
            cursor.Advance();
            expression = DomainExpression.Literal(42L);
            return true;
        }
    }

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

        await Assert.That(expr).IsTypeOf<Poly.DomainModeling.And>();
        var and = (Poly.DomainModeling.And)expr;
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
        var inputs = DomainInputBuilder.Create()
            .RegisterExpressionForm(new MagicLiteralForm())
            .BuildParserInputs();

        var expr = DslExpressionFragment.ParseExpressionFragment("MAGIC == 42", inputs);

        await Assert.That(expr).IsTypeOf<Comparison>();
        var cmp = (Comparison)expr;
        await Assert.That(cmp.Left).IsTypeOf<Literal>();
        await Assert.That(((Literal)cmp.Left).Value).IsEqualTo(42L);
    }
}