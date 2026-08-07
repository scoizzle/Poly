using Poly.DomainModeling;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Parsing;
using Poly.Grammar;

namespace Poly.Tests.Grammar;

/// <summary>E1: expression module + open form registry (temporal-ready seam).</summary>
public class DslExpressionE1Tests {
    /// <summary>Pack form: identifier <c>MAGIC</c> → literal 42 without core RD edits.</summary>
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
        var result = new DomainEvolution(new Domain("_", [], [])).Apply(changes);
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
            var matcher = new Matcher<DslTokenKind>(g, reader);
            var match = matcher.TryMatch("expr-primary");
            await Assert.That(match?.PatternName).IsEqualTo(pattern);
        }

        await AssertPattern("42", "number");
        await AssertPattern("\"x\"", "string");
        await AssertPattern("true", "true");
        await AssertPattern("Name", "ident");
        await AssertPattern("(", "group");
    }
}