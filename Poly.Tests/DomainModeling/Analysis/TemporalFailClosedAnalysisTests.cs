using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.ContractFill;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Libraries.Storage;
using Poly.DomainModeling.Libraries.Temporal;
using Poly.DomainModeling.Lowering;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// p1-4: analysis fail-closed temporal rules (design-lock negatives).
///
///  - Unknown units (<c>12 fortnights</c>) fail closed at parse — never a vacuous
///    <c>DateOperation</c> and never a dropped unit.
///  - <c>Date + Date</c> (clock nodes or Date properties) is rejected at analysis.
///  - A bare <c>Number + days</c> / <c>assign to 3 Days</c> without a temporal left
///    operand is an unresolved specialization — rejected at analysis and fail-loud at
///    lowering, never a silent numeric constant. This includes a <b>Number property</b>
///    folded to a <c>DateOperation</c> at parse (<c>Qty + 3 Days</c>): analysis rejects
///    the non-date date operand even when the other comparison side is a Date.
///  - Without the temporal pack, <c>Now</c> stays <see cref="PropertyAccess"/> (never
///    lowered as a clock) and temporal authoring is rejected at analysis.
/// </summary>
public class TemporalFailClosedAnalysisTests {
    private static EvolutionResult Evolve(string poly, DomainSession? parserInputs = null) {
        var parser = parserInputs is null ? new PolyDslParser(poly) : new PolyDslParser(poly, parserInputs);
        var changes = parser.Parse();
        return new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
    }

    private static List<string> Errors(EvolutionResult result) =>
        result.Analysis.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.Message)
            .ToList();

    [Test]
    public async Task UnknownUnit_FortnightsInPolicyDocument_FailsClosedAtParse() {
        await Assert.That(() => Evolve("""
            domain T
            Item: entity {
              Expiry: Date
              Bad: policy { Expiry < Now - 12 Fortnights }
            }
            """, ExtensionCatalog.Core.Language))
            .Throws<FormatException>();
    }

    [Test]
    public async Task DatePlusDate_ClockNodes_Policy_ReportsArithmeticError() {
        var result = Evolve("""
            domain T
            uses temporal
            Item: entity {
              Bad: policy { Now + Today is 1 }
            }
            """, ExtensionCatalog.Core.Language);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(Errors(result).Any(e =>
            e.Contains("not numeric", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task DatePlusDate_TwoDateProperties_Policy_ReportsArithmeticError() {
        var result = Evolve("""
            domain T
            Item: entity {
              Started: Date
              Finished: Date
              Bad: policy { Started + Finished > Started }
            }
            """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(Errors(result).Any(e =>
            e.Contains("not numeric", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task NumberPlusDays_NoTemporalLeftOperand_Policy_ReportsDurationError() {
        var result = Evolve("""
            domain T
            uses temporal
            Item: entity {
              Qty: Number
              Bad: policy { Qty > 5 + 3 Days }
            }
            """, ExtensionCatalog.Core.Language);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(Errors(result).Any(e =>
            e.Contains("duration", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task BareDuration_AssignWithoutTemporalLeftOperand_ReportsDurationError() {
        var result = Evolve("""
            domain T
            uses temporal
            Item: entity {
              Qty: Number
              Go: action {
                assign Qty to 3 Days
              }
            }
            """, ExtensionCatalog.Core.Language);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(Errors(result).Any(e =>
            e.Contains("duration", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task NumberPropertyPlusDays_NoTemporalLeftOperand_Policy_ReportsDateError() {
        // The parser fold (TryFoldDateOperation) is syntactic — it folds any
        // PropertyAccess + `N days` into a DateOperation before property types are
        // known. Analysis must reject a folded Number property date operand, even
        // when the comparison right operand is itself a Date (which would otherwise
        // make the folded operand look compatible).
        var result = Evolve("""
            domain T
            uses temporal
            Item: entity {
              Qty: Number
              Expiry: Date
              Bad: policy { Qty + 3 Days > Expiry }
            }
            """, ExtensionCatalog.Core.Language);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(Errors(result).Any(e =>
            e.Contains("date left operand", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task DurationPropertyPlusDays_ConstructedIr_ReportsDateError() {
        // Duration is not a DSL primitive (parser treats `Hold: Duration` as a nav),
        // but IR-constructed Duration properties must not fold as date operands.
        var item = new Entity("Item",
            [
                new Property("Hold", new DomainTypeReference("Duration"), []),
                new Property("Expiry", new DomainTypeReference("Date"), []),
            ],
            [],
            [new Policy("Bad", DomainExpression.GreaterThan(
                new DateOperation(
                    DomainExpression.Property("Hold"),
                    DomainExpression.Literal(3L, new DomainTypeReference("Number")),
                    DateOperationKind.AddDays),
                DomainExpression.Property("Expiry")))],
            []);
        var analysis = DomainModelAnalyzer.Analyze(
            DomainTestFactory.Create("T", [item], []) with { Extensions = [ExtensionCatalog.TemporalId] });

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error
            && d.Message.Contains("date left operand", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task TimePropertyPlusDays_ConstructedIr_ReportsDateError() {
        var item = new Entity("Item",
            [
                new Property("Opens", new DomainTypeReference("Time"), []),
                new Property("Expiry", new DomainTypeReference("Date"), []),
            ],
            [],
            [new Policy("Bad", DomainExpression.GreaterThan(
                new DateOperation(
                    DomainExpression.Property("Opens"),
                    DomainExpression.Literal(3L, new DomainTypeReference("Number")),
                    DateOperationKind.AddDays),
                DomainExpression.Property("Expiry")))],
            []);
        var analysis = DomainModelAnalyzer.Analyze(
            DomainTestFactory.Create("T", [item], []) with { Extensions = [ExtensionCatalog.TemporalId] });

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error
            && d.Message.Contains("date left operand", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task NumberPropertyPlusDays_NoTemporalLeftOperand_AssignRhs_ReportsDateError() {
        var result = Evolve("""
            domain T
            uses temporal
            Item: entity {
              Qty: Number
              Expiry: Date
              Go: action {
                assign Expiry to Qty + 3 Days
              }
            }
            """, ExtensionCatalog.Core.Language);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(Errors(result).Any(e =>
            e.Contains("date left operand", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task Duration_UnresolvedSpecializationAtLowering_FailsLoud() {
        var pass = new DomainExpressionLoweringPass();
        await Assert.That(() => pass.Lower(new Duration(12, DurationUnit.Days), new ParameterReference()))
            .Throws<NotSupportedException>();
    }

    [Test]
    public async Task Now_WithoutTemporalPack_PolicyReference_ReportsUnknownProperty() {
        var result = Evolve("""
            domain T
            Item: entity {
              Expiry: Date
              IsExpired: policy { Expiry < Now }
            }
            """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(Errors(result).Any(e =>
            e.Contains("'Now'", StringComparison.Ordinal)
            && e.Contains("does not exist", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task Now_WithoutTemporalPack_ExpressionLowersAsPropertyAccess_NotClock() {
        var expr = DslExpressionFragment.ParseExpressionFragment("Now");
        await Assert.That(expr).IsTypeOf<PropertyAccess>();

        var lowered = new DomainExpressionLoweringPass().Lower(expr, new ParameterReference());
        await Assert.That(lowered).IsTypeOf<Member>();
        var member = (Member)lowered;
        await Assert.That(member.MemberName).IsEqualTo("Now");
        await Assert.That(member.MemberName).IsNotEqualTo("UtcNow");
    }

    [Test]
    public async Task Duration_WithoutTemporalPack_ParseFailsClosed() {
        await Assert.That(() => DslExpressionFragment.ParseExpressionFragment("5 Days"))
            .Throws<FormatException>();
    }

    [Test]
    public async Task Default_TodayOnText_WithTemporalPack_Rejected() {
        var result = Evolve("""
            domain T
            uses temporal
            Event: entity { Label: Text default(Today) }
            """, ExtensionCatalog.Core.Language);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(Errors(result).Any(e =>
            e.Contains("default(Today)", StringComparison.Ordinal)
            && e.Contains("not compatible", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task Default_TodayOnDate_WithTemporalPack_Succeeds() {
        var result = Evolve("""
            domain T
            uses temporal
            Event: entity { RecordedOn: Date default(Today) }
            """, ExtensionCatalog.Core.Language);

        await Assert.That(result.Succeeded).IsTrue();
        var expr = result.Root!.Types.OfType<Entity>().Single()
            .Properties.Single().Constraints.OfType<Poly.DomainModeling.Ontology.Constraints.DefaultValueConstraint>()
            .Single().Expression;
        await Assert.That(expr).IsTypeOf<Today>();
    }
}