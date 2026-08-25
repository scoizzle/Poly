using Poly.DomainModeling;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.Grammar;

namespace Poly.Tests.Grammar;

/// <summary>
/// E1: expression module + open form registry (temporal-ready seam).
/// pack-shaped open forms: Grammar patterns + session fold + named print fills.
/// </summary>
public class DslExpressionE1Tests {
    private static bool IsMagicIdentifier(DslToken token) =>
        token.Kind == DslTokenKind.Identifier
        && string.Equals(token.Text, "MAGIC", StringComparison.Ordinal);

    /// <summary>
    /// Pack IR for MAGIC: a tiny test-only node. The print binder owns
    /// <see cref="MagicLiteral"/> (a distinct expression type), so it never collides
    /// with the core <see cref="Literal"/> binder — step 3's "tiny test-only node"
    /// fallback, since a pack binder matching Literal(42) would be rejected as a
    /// duplicate owner by <see cref="ExpressionPrintRegistry"/>.
    /// </summary>
    private sealed record MagicLiteral(long Value) : DomainExpression;

    /// <summary>Pack IR for <c>N unit</c>: a tiny test-only node (no product temporal IR).</summary>
    private sealed record DurationLiteral(string Text) : DomainExpression;

    private sealed class MagicBinder : IExpressionPrintMapping {
        public Type ExpressionType => typeof(MagicLiteral);

        public bool TryMap(DomainExpression expression, out PrintMapping binding) {
            if (expression is not MagicLiteral) {
                binding = default;
                return false;
            }
            binding = new PrintMapping(
                "expr-primary",
                "magic",
                NamedFills: new Dictionary<string, string>(StringComparer.Ordinal) { ["magic"] = "MAGIC" });
            return true;
        }
    }

    private sealed class DurationBinder : IExpressionPrintMapping {
        public Type ExpressionType => typeof(DurationLiteral);

        public bool TryMap(DomainExpression expression, out PrintMapping binding) {
            if (expression is not DurationLiteral duration) {
                binding = default;
                return false;
            }
            var parts = duration.Text.Split(' ', 2);
            binding = new PrintMapping(
                "expr-primary",
                "duration",
                NamedFills: new Dictionary<string, string>(StringComparer.Ordinal) {
                    ["amount"] = parts[0],
                    ["unit"] = parts.Length > 1 ? parts[1] : "",
                });
            return true;
        }
    }

    private static DomainSession BuildMagicPackInputs() {
        var builder = SessionBuilder.CreateEmpty();
        foreach (var rule in new[] { "expr-primary", "expr-primary-no-not" })
            builder.ExpressionForms.RegisterFold(rule, "magic", _ => new MagicLiteral(42));
        builder.ExpressionForms.RegisterPrintMapping(new MagicBinder());
        builder.ExpressionForms.RegisterGrammarContributor(g => {
            foreach (var rule in new[] { "expr-primary", "expr-primary-no-not" }) {
                g.Define(rule)
                    .Pattern("magic", priority: 1)
                    .Predicate(IsMagicIdentifier, "magic")
                    .Commit();
            }
        });
        return builder.Build();
    }

    private static DomainSession BuildDurationPackInputs() {
        var builder = SessionBuilder.CreateEmpty();
        foreach (var rule in new[] { "expr-primary", "expr-primary-no-not" }) {
            builder.ExpressionForms.RegisterFold(rule, "duration", match => {
                var amount = match.Captures["amount"][0].Text;
                var unit = match.Captures["unit"][0].Text;
                return new DurationLiteral($"{amount} {unit}");
            });
        }
        builder.ExpressionForms.RegisterPrintMapping(new DurationBinder());
        return builder.Build();
    }

    [Test]
    public async Task OpenForm_MagicIdentifier_ParsesAndPrintsAndReparses() {
        var inputs = BuildMagicPackInputs();

        var poly = """
            domain D
            E: entity {
              P: policy { MAGIC == 42 }
            }
            """;

        var changes = new PolyDslParser(poly, inputs).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        var cmp = (Comparison)result.Root!.Types.OfType<Entity>().Single().Policies.Single().Expression;
        await Assert.That(cmp.Left).IsTypeOf<MagicLiteral>();
        await Assert.That(((MagicLiteral)cmp.Left).Value).IsEqualTo(42L);

        var printed = new DomainDslPrinter(inputs).Print(result.Root!);
        await Assert.That(printed.Contains("MAGIC is 42", StringComparison.Ordinal)).IsTrue();

        var reparsed = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .Apply(new PolyDslParser(printed, inputs).Parse());
        await Assert.That(reparsed.Succeeded).IsTrue();
        var cmp2 = (Comparison)reparsed.Root!.Types.OfType<Entity>().Single().Policies.Single().Expression;
        await Assert.That(cmp2.Left).IsTypeOf<MagicLiteral>();
        await Assert.That(((MagicLiteral)cmp2.Left).Value).IsEqualTo(42L);
    }

    [Test]
    public async Task OpenForm_NumberUnit_ParsesAndPrintsAndReparses() {
        var inputs = BuildDurationPackInputs();

        var poly = """
            domain D
            E: entity {
              P: policy { 12 Days == 12 Days }
            }
            """;

        var changes = new PolyDslParser(poly, inputs).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        var cmp = (Comparison)result.Root!.Types.OfType<Entity>().Single().Policies.Single().Expression;
        await Assert.That(cmp.Left).IsTypeOf<DurationLiteral>();
        await Assert.That(((DurationLiteral)cmp.Left).Text).IsEqualTo("12 Days");
        await Assert.That(((DurationLiteral)cmp.Right).Text).IsEqualTo("12 Days");

        var printed = new DomainDslPrinter(inputs).Print(result.Root!);
        await Assert.That(printed.Contains("12 Days is 12 Days", StringComparison.Ordinal)).IsTrue();

        var reparsed = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .Apply(new PolyDslParser(printed, inputs).Parse());
        await Assert.That(reparsed.Succeeded).IsTrue();
        var cmp2 = (Comparison)reparsed.Root!.Types.OfType<Entity>().Single().Policies.Single().Expression;
        await Assert.That(cmp2.Left).IsTypeOf<DurationLiteral>();
        await Assert.That(((DurationLiteral)cmp2.Left).Text).IsEqualTo("12 Days");
        await Assert.That(((DurationLiteral)cmp2.Right).Text).IsEqualTo("12 Days");
    }

    [Test]
    public async Task WithoutPattern_MagicIsPropertyAccess() {
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
    /// pack-1-4: pack patterns register on BOTH primary rules (with and without <c>not</c>),
    /// the matcher recognizes them, and the full expression still matches end to end.
    /// The <c>magic</c> pattern is predicate-led + an optional comma so it wins the
    /// longest-match tie against the core <c>ident</c> pattern deterministically.
    /// </summary>
    [Test]
    public async Task PackPattern_MagicAndNumberUnit_RegisterOnBothPrimaries() {
        var g = DslGrammar.Build(grammar => {
            foreach (var rule in new[] { "expr-primary", "expr-primary-no-not" }) {
                grammar.Define(rule)
                    .Pattern("magic", priority: 1)
                    .Predicate(IsMagicIdentifier, "magic")
                    .Commit();
            }
        });

        foreach (var rule in new[] { "expr-primary", "expr-primary-no-not" }) {
            await Assert.That(g.GetPatterns(rule).Any(p => p.Name == "magic")).IsTrue();
            await Assert.That(g.GetPatterns(rule).Any(p => p.Name == "duration")).IsTrue();

            var magic = new Matcher<DslToken, DslTokenKind>(g, new DslTokenReader("MAGIC"));
            var magicPrimary = magic.TryMatch(rule);
            await Assert.That(magicPrimary?.PatternName).IsEqualTo("magic");
            await Assert.That(magicPrimary!.Consumed).IsEqualTo(1);

            var duration = new Matcher<DslToken, DslTokenKind>(g, new DslTokenReader("12 Days"));
            var durationPrimary = duration.TryMatch(rule);
            await Assert.That(durationPrimary?.PatternName).IsEqualTo("duration");
            await Assert.That(durationPrimary!.Consumed).IsEqualTo(2);
        }

        var full = new Matcher<DslToken, DslTokenKind>(g, new DslTokenReader("MAGIC == 42"));
        var expr = full.TryMatch("expr");
        await Assert.That(expr).IsNotNull();
        await Assert.That(expr!.Consumed).IsEqualTo(3);
    }
}