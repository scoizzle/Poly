using Poly.DomainModeling;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.ContractFill;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Libraries.Storage;
using Poly.DomainModeling.Libraries.Temporal;
using Poly.Grammar;

namespace Poly.Tests.DomainModeling.Packs;

/// <summary>
/// pack-3a: temporal spellings (<c>Now - 12 Days</c>, <c>DueDate + 14 Days</c>,
/// <c>ExpiryDate &lt; Now</c>) survive export_dsl. The temporal pack registers
/// Grammar patterns on both primaries and print binders for <c>Now</c>/<c>Today</c>/
/// <c>DateOperation</c>; a session without the pack fails closed on both sides
/// (parse rejects, DateOperation print throws).
/// </summary>
public sealed class TemporalPackPrintRoundTripTests {
    private static DomainSession TemporalInputs() =>
        ExtensionCatalog.Core.Language;

    private const string TemporalDomain = """
        domain TemporalDomain

        Loan: entity {
          DueDate: Date
          ExpiryDate: Date
          Recent: policy { Now - 12 Days > DueDate }
          Renew: policy { DueDate + 14 Days > ExpiryDate }
          Expired: policy { ExpiryDate < Now }
        }
        """;

    private static Domain Apply(string poly, DomainSession inputs) {
        var changes = new PolyDslParser(poly, inputs).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Apply of temporal domain failed: {result.FailureSummary}");
        return result.Root!;
    }

    private static DomainExpression PolicyExpression(Domain domain, string policyName) =>
        domain.Types.OfType<Entity>().Single().Policies
            .Single(p => p.Name == policyName).Expression;

    /// <summary>Normalized structural text for IR-equivalence (pack-3a scope).</summary>
    private static string Canonical(DomainExpression e) => e switch {
        PropertyAccess p => $"Prop({p.Name})",
        Literal l => $"Lit({l.Value})",
        Now => "Now",
        Today => "Today",
        DateOperation d => $"DateOp({Canonical(d.Date)},{Canonical(d.Offset)},{d.Kind})",
        Comparison c => $"Cmp({Canonical(c.Left)},{c.Kind},{Canonical(c.Right)})",
        _ => throw new InvalidOperationException(
            $"Unmapped DomainExpression subtype '{e.GetType().Name}' in canonical oracle"),
    };

    // ── Round-trip goldens ───────────────────────────────────────

    [Test]
    public async Task TemporalPolyDslRoundTrip_NowMinus12Days_ParsesToDateOperation() {
        var first = Apply(TemporalDomain, TemporalInputs());
        var expr = PolicyExpression(first, "Recent");

        await Assert.That(expr).IsTypeOf<Comparison>();
        var cmp = (Comparison)expr;
        await Assert.That(cmp.Left).IsTypeOf<DateOperation>();
        var dateOp = (DateOperation)cmp.Left;
        await Assert.That(dateOp.Date).IsTypeOf<Now>();
        await Assert.That(dateOp.Kind).IsEqualTo(DateOperationKind.AddDays);
        await Assert.That(((Literal)dateOp.Offset).Value).IsEqualTo(-12L);
    }

    [Test]
    public async Task TemporalPolyDslRoundTrip_DueDatePlus14Days_ParsesToDateOperation() {
        var first = Apply(TemporalDomain, TemporalInputs());
        var expr = PolicyExpression(first, "Renew");

        await Assert.That(expr).IsTypeOf<Comparison>();
        var cmp = (Comparison)expr;
        await Assert.That(cmp.Left).IsTypeOf<DateOperation>();
        var dateOp = (DateOperation)cmp.Left;
        await Assert.That(dateOp.Date).IsTypeOf<PropertyAccess>();
        await Assert.That(((PropertyAccess)dateOp.Date).Name).IsEqualTo("DueDate");
        await Assert.That(dateOp.Kind).IsEqualTo(DateOperationKind.AddDays);
        await Assert.That(((Literal)dateOp.Offset).Value).IsEqualTo(14L);
    }

    [Test]
    public async Task TemporalPolyDslRoundTrip_ExpiryDateLessThanNow_ParsesToComparison() {
        var first = Apply(TemporalDomain, TemporalInputs());
        var expr = PolicyExpression(first, "Expired");

        await Assert.That(expr).IsTypeOf<Comparison>();
        var cmp = (Comparison)expr;
        await Assert.That(cmp.Kind).IsEqualTo(ComparisonKind.LessThan);
        await Assert.That(cmp.Right).IsTypeOf<Now>();
    }

    [Test]
    public async Task TemporalPolyDslRoundTrip_ThreeSpellings_PrintAndReparse_IREquivalent() {
        var inputs = TemporalInputs();
        var first = Apply(TemporalDomain, inputs);

        var printed = new DomainDslPrinter(inputs).Print(first);
        await Assert.That(printed.Contains("Now - 12 Days", StringComparison.Ordinal)).IsTrue();
        await Assert.That(printed.Contains("DueDate + 14 Days", StringComparison.Ordinal)).IsTrue();
        await Assert.That(printed.Contains("ExpiryDate < Now", StringComparison.Ordinal)).IsTrue();

        var second = Apply(printed, inputs);
        await Assert.That(second.Types.OfType<Entity>().Count).IsEqualTo(1);
        var firstEntity = first.Types.OfType<Entity>().Single();
        var secondEntity = second.Types.OfType<Entity>().Single();
        await Assert.That(secondEntity.Policies.Count).IsEqualTo(firstEntity.Policies.Count);
        foreach (var p1 in firstEntity.Policies) {
            var p2 = secondEntity.Policies.Single(p => p.Name == p1.Name);
            await Assert.That(Canonical(p2.Expression)).IsEqualTo(Canonical(p1.Expression));
        }
    }

    // ── Binder + grammar-pattern registration ────────────────────

    [Test]
    public async Task TemporalPrint_WithPack_NowTodayDateOperation_PrintProductSpelling() {
        var printer = new DomainDslPrinter(TemporalInputs());

        await Assert.That(printer.PrintTestExpression(new Now())).IsEqualTo("Now");
        await Assert.That(printer.PrintTestExpression(new Today())).IsEqualTo("Today");
        await Assert.That(printer.PrintTestExpression(
            new DateOperation(DomainExpression.Property("DueDate"), DomainExpression.Literal(14), DateOperationKind.AddDays)))
            .IsEqualTo("DueDate + 14 Days");
        await Assert.That(printer.PrintTestExpression(
            new DateOperation(new Now(), DomainExpression.Literal(-12L), DateOperationKind.AddDays)))
            .IsEqualTo("Now - 12 Days");
        await Assert.That(printer.PrintTestExpression(
            new DateOperation(new Today(), DomainExpression.Literal(-3L), DateOperationKind.AddMonths)))
            .IsEqualTo("Today - 3 Months");
    }

    [Test]
    public async Task TemporalPack_GrammarPatterns_RegisterOnBothPrimaries() {
        var inputs = TemporalInputs();
        var g = DslGrammar.Build(grammar => inputs.ExpressionForms.ContributeGrammarPatterns(grammar));

        foreach (var rule in new[] { "expr-primary", "expr-primary-no-not" }) {
            await Assert.That(g.GetPatterns(rule).Any(p => p.Name == "now")).IsTrue();
            await Assert.That(g.GetPatterns(rule).Any(p => p.Name == "today")).IsTrue();
            await Assert.That(g.GetPatterns(rule).Any(p => p.Name == "duration")).IsTrue();

            var now = new Matcher<DslToken, DslTokenKind>(g, new DslTokenReader("Now"));
            await Assert.That(now.TryMatch(rule)?.PatternName).IsEqualTo("now");

            var today = new Matcher<DslToken, DslTokenKind>(g, new DslTokenReader("Today"));
            await Assert.That(today.TryMatch(rule)?.PatternName).IsEqualTo("today");

            var duration = new Matcher<DslToken, DslTokenKind>(g, new DslTokenReader("12 Days"));
            await Assert.That(duration.TryMatch(rule)?.PatternName).IsEqualTo("duration");
        }
    }

    // ── Missing pack fails closed both ways ──────────────────────

    [Test]
    public async Task TemporalParse_WithoutPack_NowMinusDays_Rejects() {
        await Assert.That(() => DslExpressionFragment.ParseExpressionFragment("Now - 12 Days"))
            .Throws<FormatException>();
        await Assert.That(() => new PolyDslParser(TemporalDomain).Parse())
            .Throws<FormatException>();
    }

    [Test]
    public async Task TemporalPrint_WithoutPack_DateOperation_Throws() {
        var domain = Apply(TemporalDomain, TemporalInputs());

        var noInputsPrinter = new DomainDslPrinter();
        await Assert.That(() => noInputsPrinter.Print(domain))
            .Throws<InvalidOperationException>();

        var defaultInputsPrinter = new DomainDslPrinter(DomainSession.ForExtensions([]));
        await Assert.That(() => defaultInputsPrinter.Print(domain))
            .Throws<InvalidOperationException>();
    }
}