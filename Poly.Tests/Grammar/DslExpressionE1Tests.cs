using Poly.DomainModeling;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Parsing;
using Poly.Grammar;

namespace Poly.Tests.Grammar;

/// <summary>E1: expression module + open form registry (temporal-ready seam).</summary>
public class DslExpressionE1Tests {
    /// <summary>Pack form: identifier <c>MAGIC</c> → literal 42 without core edits.</summary>
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

    /// <summary>Pack form: <c>Number Identifier</c> → literal carrying the unit (live path).</summary>
    private sealed class DurationLiteralForm : IExpressionPrimaryForm {
        public bool TryParse(IDslParseCursor cursor, DslExpressionParser expressions, out DomainExpression expression) {
            expression = null!;
            if (cursor.Current.Kind != DslTokenKind.Number
                || cursor.Peek(1).Kind != DslTokenKind.Identifier)
                return false;
            var num = cursor.Current.Text;
            var unit = cursor.Peek(1).Text;
            cursor.Advance();
            cursor.Advance();
            expression = DomainExpression.Literal($"{num} {unit}");
            return true;
        }
    }

    [Test]
    public async Task OpenForm_MagicIdentifier_ParsesInPolicyWithoutCoreEdit() {
        var inputs = DomainInputBuilder.Create()
            .RegisterExpressionForm(new MagicLiteralForm())
            .BuildParserInputs();

        var poly = """
            domain D
            E: entity {
              P: policy { MAGIC == 42 }
            }
            """;

        var changes = new PolyDslParser(poly, inputs).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        var policy = result.Root!.Types.OfType<Entity>().Single().Policies.Single();
        await Assert.That(policy.Expression).IsTypeOf<Comparison>();
        var cmp = (Comparison)policy.Expression;
        await Assert.That(cmp.Kind).IsEqualTo(ComparisonKind.Equal);
        await Assert.That(cmp.Left).IsTypeOf<Literal>();
        await Assert.That(((Literal)cmp.Left).Value).IsEqualTo(42L);
    }

    [Test]
    public async Task WithoutOpenForm_MagicIsPropertyAccess() {
        var poly = """
            domain D
            E: entity {
              P: policy { MAGIC == 42 }
            }
            """;

        var changes = new PolyDslParser(poly).Parse();
        var addPolicy = changes.OfType<AddPolicyToEntityChange>().Single();
        await Assert.That(addPolicy.Policy.Expression).IsTypeOf<Comparison>();
        var cmp = (Comparison)addPolicy.Policy.Expression;
        await Assert.That(cmp.Left).IsTypeOf<PropertyAccess>();
        await Assert.That(((PropertyAccess)cmp.Left).Name).IsEqualTo("MAGIC");
    }

    [Test]
    public async Task ExprPrimary_GrammarRule_RecognizesLiterals() {
        var g = DslGrammar.Build();
        async Task AssertPattern(string text, string pattern) {
            var reader = new DslTokenReader(text);
            var matcher = new Matcher<DslToken, DslTokenKind>(g, reader);
            var match = matcher.TryMatch("expr-primary");
            await Assert.That(match?.PatternName).IsEqualTo(pattern);
        }

        await AssertPattern("42", "number");
        await AssertPattern("\"x\"", "string");
        await AssertPattern("true", "true");
        await AssertPattern("Name", "ident");
        await AssertPattern("(1 + 2)", "group");
    }

    /// <summary>
    /// gpure-6 parity: a temporal-style pack registers a Number &lt;unit&gt;
    /// pattern on both primary rules + drives the live path via its form. Pattern
    /// registration covers the Matcher/probe surface; the LIVE path is the handler
    /// form — prove both, matching the v1 S4 test.
    /// </summary>
    [Test]
    public async Task PackPattern_NumberUnit_ExtendsPrimarySurface() {
        var g = DslGrammar.Build(grammar => {
            foreach (var rule in new[] { "expr-primary", "expr-primary-no-not" }) {
                grammar.Define(rule)
                    .Pattern("duration").Kind(DslTokenKind.Number).Value(DslTokenKind.Identifier).Commit();
            }
        });
        var matcher = new Matcher<DslToken, DslTokenKind>(g, new DslTokenReader("12 days"));
        var primary = matcher.TryMatch("expr-primary");
        await Assert.That(primary?.PatternName).IsEqualTo("duration");
        await Assert.That(primary!.Consumed).IsEqualTo(2);
        var full = matcher.TryMatch("expr");
        await Assert.That(full).IsNotNull();
        await Assert.That(full!.Consumed).IsEqualTo(2);

        // Live path: the same pack, driven by its form, parses a full policy end to end.
        var inputs = DomainInputBuilder.Create()
            .RegisterExpressionForm(new DurationLiteralForm())
            .BuildParserInputs();
        var poly = """
            domain D
            E: entity {
              P: policy { 12 days == 12 days }
            }
            """;
        var changes = new PolyDslParser(poly, inputs).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();
        var policy = result.Root!.Types.OfType<Entity>().Single().Policies.Single();
        var cmp = (Comparison)policy.Expression;
        await Assert.That(cmp.Kind).IsEqualTo(ComparisonKind.Equal);
        await Assert.That(((Literal)cmp.Left).Value).IsEqualTo("12 days");
        await Assert.That(((Literal)cmp.Right).Value).IsEqualTo("12 days");
    }
}