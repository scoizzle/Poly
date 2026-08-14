using Poly.DomainModeling;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Parsing;
using Poly.Grammar;

namespace Poly.Tests.Grammar;

/// <summary>
/// E1: expression module + open form registry (temporal-ready seam).
/// pack-1-4: pack-shaped open forms are Grammar patterns + fold + print binder.
/// The matcher recognizes the pack pattern on both primary rules; the fold is the
/// cited-gap RD escape (<see cref="IExpressionPrimaryForm"/>, pack-host lock 13 —
/// the product expr parser is recursive descent and cannot fold by grammar pattern
/// name); the print binder re-emits the pack spelling so reparse hits the form again.
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

    /// <summary>Fold (cited-gap RD escape): identifier <c>MAGIC</c> → <see cref="MagicLiteral"/>.</summary>
    private sealed class MagicForm : IExpressionPrimaryForm {
        public bool TryParse(IDslParseCursor cursor, DslExpressionParser expressions, out DomainExpression expression) {
            expression = null!;
            if (cursor.Current.Kind != DslTokenKind.Identifier
                || !string.Equals(cursor.Current.Text, "MAGIC", StringComparison.Ordinal))
                return false;
            cursor.Advance();
            expression = new MagicLiteral(42);
            return true;
        }
    }

    /// <summary>Fold (cited-gap RD escape): <c>Number Identifier</c> → <see cref="DurationLiteral"/>.</summary>
    private sealed class DurationForm : IExpressionPrimaryForm {
        public bool TryParse(IDslParseCursor cursor, DslExpressionParser expressions, out DomainExpression expression) {
            expression = null!;
            if (cursor.Current.Kind != DslTokenKind.Number
                || cursor.Peek(1).Kind != DslTokenKind.Identifier)
                return false;
            var num = cursor.Current.Text;
            var unit = cursor.Peek(1).Text;
            cursor.Advance();
            cursor.Advance();
            expression = new DurationLiteral($"{num} {unit}");
            return true;
        }
    }

    /// <summary>Pack-scoped print binder: <see cref="MagicLiteral"/> prints as the <c>MAGIC</c> identifier.</summary>
    private sealed class MagicBinder : IExpressionPrintMapping {
        public Type ExpressionType => typeof(MagicLiteral);

        public bool TryMap(DomainExpression expression, out PrintMapping binding) {
            if (expression is not MagicLiteral) {
                binding = default;
                return false;
            }
            var at = 0;
            binding = new PrintMapping("expr-primary", "magic", ctx => {
                if (at++ == 0)
                    ctx.Emit("MAGIC");
            });
            return true;
        }
    }

    /// <summary>Pack-scoped print binder: <see cref="DurationLiteral"/> prints as <c>N unit</c>.</summary>
    private sealed class DurationBinder : IExpressionPrintMapping {
        public Type ExpressionType => typeof(DurationLiteral);

        public bool TryMap(DomainExpression expression, out PrintMapping binding) {
            if (expression is not DurationLiteral duration) {
                binding = default;
                return false;
            }
            var parts = duration.Text.Split(' ', 2);
            var at = 0;
            binding = new PrintMapping("expr-primary", "duration", ctx => {
                ctx.Emit(parts[at++ % parts.Length]);
            });
            return true;
        }
    }

    private static DomainParserInputs BuildMagicPackInputs() {
        var builder = DomainHostBuilder.CreateEmpty();
        builder.ExpressionForms.Register(new MagicForm());
        builder.ExpressionForms.RegisterPrintMapping(new MagicBinder());
        builder.ExpressionForms.RegisterGrammarContributor(g => {
            foreach (var rule in new[] { "expr-primary", "expr-primary-no-not" }) {
                g.Define(rule)
                    .Pattern("magic")
                    .Predicate(IsMagicIdentifier, "magic-identifier")
                    .Optional(DslTokenKind.Comma)
                    .Commit();
            }
        });
        return builder.BuildParserInputs();
    }

    private static DomainParserInputs BuildDurationPackInputs() {
        var builder = DomainHostBuilder.CreateEmpty();
        builder.ExpressionForms.Register(new DurationForm());
        builder.ExpressionForms.RegisterPrintMapping(new DurationBinder());
        builder.ExpressionForms.RegisterGrammarContributor(g => {
            foreach (var rule in new[] { "expr-primary", "expr-primary-no-not" }) {
                g.Define(rule)
                    .Pattern("duration")
                    .Value(DslTokenKind.Number).Value(DslTokenKind.Identifier)
                    .Commit();
            }
        });
        return builder.BuildParserInputs();
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
              P: policy { 12 days == 12 days }
            }
            """;

        var changes = new PolyDslParser(poly, inputs).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        var cmp = (Comparison)result.Root!.Types.OfType<Entity>().Single().Policies.Single().Expression;
        await Assert.That(cmp.Left).IsTypeOf<DurationLiteral>();
        await Assert.That(((DurationLiteral)cmp.Left).Text).IsEqualTo("12 days");
        await Assert.That(((DurationLiteral)cmp.Right).Text).IsEqualTo("12 days");

        var printed = new DomainDslPrinter(inputs).Print(result.Root!);
        await Assert.That(printed.Contains("12 days is 12 days", StringComparison.Ordinal)).IsTrue();

        var reparsed = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .Apply(new PolyDslParser(printed, inputs).Parse());
        await Assert.That(reparsed.Succeeded).IsTrue();
        var cmp2 = (Comparison)reparsed.Root!.Types.OfType<Entity>().Single().Policies.Single().Expression;
        await Assert.That(cmp2.Left).IsTypeOf<DurationLiteral>();
        await Assert.That(((DurationLiteral)cmp2.Left).Text).IsEqualTo("12 days");
        await Assert.That(((DurationLiteral)cmp2.Right).Text).IsEqualTo("12 days");
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
                    .Pattern("magic")
                    .Predicate(IsMagicIdentifier, "magic-identifier")
                    .Optional(DslTokenKind.Comma)
                    .Commit();
                grammar.Define(rule)
                    .Pattern("duration")
                    .Value(DslTokenKind.Number).Value(DslTokenKind.Identifier)
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

            var duration = new Matcher<DslToken, DslTokenKind>(g, new DslTokenReader("12 days"));
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