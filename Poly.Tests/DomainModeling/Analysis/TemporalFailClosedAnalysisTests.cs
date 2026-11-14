using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Tests.TestHelpers;

using DmAction = Poly.DomainModeling.Ontology.Action;

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
        return new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
    }

    private static List<string> Errors(EvolutionResult result) =>
        result.Analysis.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.Message)
            .ToList();

    private static DomainType[] TemporalPrimitives() =>
        [.. TemporalTypeCatalog.Definitions.Select(static d => new PrimitiveType(d.Name, d.Semantics, []))];

    private static AnalysisResult AnalyzeAssign(string targetType, string sourceType) {
        var item = new Entity("Item",
            [
                new Property("Target", new DomainTypeReference(targetType), []),
                new Property("Source", new DomainTypeReference(sourceType), []),
            ],
            [new DmAction("Stamp", InvocationResult.Void, [],
                [new AssignEffect(DomainExpression.Property("Target"), DomainExpression.Property("Source"))],
                [])],
            [],
            []);
        return DomainModelAnalyzer.Analyze(
            DomainTestFactory.Create("T", [.. TemporalPrimitives(), item], []) with {
                Extensions = [ExtensionCatalog.TemporalId]
            });
    }

    private static bool HasAssignMismatch(AnalysisResult analysis, string targetProperty) =>
        analysis.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error
            && d.Message.Contains($"type mismatch in assign to property '{targetProperty}'", StringComparison.Ordinal));

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
            uses temporal
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

    private static string DurationDocument(string members) => $$"""
        domain T
        uses temporal
        Item: entity {
          Qty: Number
          Len: Duration
          Gap: Duration optional
          {{members}}
        }
        """;

    /// <summary>
    /// T-10a R1 (#468): a bare duration is the value of an assign to a Duration property and an operand compared
    /// with one, required or optional, on either side. Everywhere else it still needs a temporal left operand.
    /// </summary>
    [Test]
    [Arguments("assign to a required Duration", "Go: action { assign Len to 30 Minutes }")]
    [Arguments("assign to an optional Duration", "Go: action { assign Gap to 2 Hours }")]
    [Arguments("compared with a required Duration", "Slow: policy { Len > 1 Hours }")]
    [Arguments("the literal on the left", "Slow: policy { 1 Hours < Len }")]
    public async Task DurationProperty_BareDurationAtAssignAndComparison_AnalyzesClean(string row, string members) {
        var result = Evolve(DurationDocument(members), ExtensionCatalog.Core.Language);

        await Assert.That(Errors(result)).IsEmpty().Because(row);
        await Assert.That(result.Succeeded).IsTrue().Because(row);
    }

    /// <summary>
    /// T-21d (#484): an optional Duration compared with a bare duration is C#'s lifted comparison, either operand order: it
    /// runs on every door (<c>AnalysisGateParityTests</c>), so analysis accepts it. A bare duration beside an optional DATE is
    /// still the pack's own refusal, naming the type it met.
    /// </summary>
    [Test]
    [Arguments("Slow: policy { Gap > 1 Hours }")]
    [Arguments("Slow: policy { Gap == 1 Hours }")]
    [Arguments("Slow: policy { 1 Hours >= Gap }")]
    public async Task OptionalDuration_ComparedWithABareDuration_IsAccepted(string members) {
        var result = Evolve(DurationDocument(members), ExtensionCatalog.Core.Language);

        await Assert.That(Errors(result)).IsEmpty();
        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task OptionalDate_ComparedWithABareDuration_KeepsTheBareDurationMessage() {
        var result = Evolve($$"""
            domain T
            uses temporal
            Item: entity {
              OptDue: Date optional
              Slow: policy { OptDue > 1 Hours }
            }
            """, ExtensionCatalog.Core.Language);

        await Assert.That(Errors(result).Any(e => e.Contains("comparison operand '1 Hours' is a bare duration with no temporal left operand (only a Duration takes it; got 'Date')", StringComparison.Ordinal)))
            .IsTrue().Because(string.Join(" | ", Errors(result)));
    }

    /// <summary>A duration beyond what a <c>TimeSpan</c> holds throws at define or run time on a host; analysis refuses it, positioned.</summary>
    [Test]
    [Arguments("Go: action { assign Len to 9223372036854775807 Hours }")]
    [Arguments("Go: action { assign Len to 9223372036854775807 Days }")]
    [Arguments("Go: action { assign Len to 9223372036854775807 Weeks }")]
    [Arguments("Go: action { assign Len to 9223372036854775807 Milliseconds }")]
    [Arguments("Slow: policy { Len > 9223372036854775807 Hours }")]
    public async Task BareDuration_BeyondATimeSpan_IsRefusedAtAnalysis(string members) {
        var result = Evolve(DurationDocument(members), ExtensionCatalog.Core.Language);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(Errors(result).Any(e => e.Contains("too long for a TimeSpan", StringComparison.Ordinal))).IsTrue()
            .Because(string.Join(" | ", Errors(result)));
    }

    /// <summary>The create initializer is the third stamp site: a bare duration for a Duration property of the created entity.</summary>
    [Test]
    [Arguments("5 Minutes", "")]
    [Arguments("2 Months", "no fixed length")]
    public async Task DurationCreateInitializer_BareDuration_IsAcceptedOrRefusedAsAnAssignIs(string value, string refusal) {
        var result = Evolve($$"""
            domain T
            uses temporal
            Job: entity {
              runs: many Run
              Spawn: action { create in runs { Budget: {{value}} } }
            }
            Run: entity {
              Budget: Duration
            }
            """, ExtensionCatalog.Core.Language);

        if (refusal.Length == 0) {
            await Assert.That(Errors(result)).IsEmpty();
            return;
        }
        await Assert.That(Errors(result).Any(e => e.Contains(refusal, StringComparison.Ordinal))).IsTrue()
            .Because(string.Join(" | ", Errors(result)));
    }

    /// <summary>Months and years have no fixed length: refused at the new sites as they are for a default.</summary>
    [Test]
    [Arguments("Go: action { assign Len to 2 Months }")]
    [Arguments("Go: action { assign Gap to 1 Years }")]
    [Arguments("Slow: policy { Len > 3 Months }")]
    [Arguments("Slow: policy { 1 Years < Len }")]
    public async Task DurationAtAssignAndComparison_MonthsOrYears_FailsAtAnalysis(string members) {
        var result = Evolve(DurationDocument(members), ExtensionCatalog.Core.Language);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(Errors(result).Any(e => e.Contains("no fixed length", StringComparison.Ordinal))).IsTrue()
            .Because(string.Join(" | ", Errors(result)));
    }

    /// <summary>#551: every other position names itself; only a default says "default value".</summary>
    [Test]
    [Arguments("Go: action { assign Qty to 3 Days }", "assign value '3 Days'")]
    [Arguments("Slow: policy { Qty > 3 Days }", "comparison operand '3 Days'")]
    [Arguments("Slow: policy { Qty > 5 + 3 Days }", "operand '3 Days'")]
    public async Task BareDuration_InPolicyAndAssign_MessageNamesThePosition(string members, string position) {
        var result = Evolve(DurationDocument(members), ExtensionCatalog.Core.Language);

        var messages = string.Join(" | ", Errors(result));
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(messages).Contains(position);
        await Assert.That(messages).DoesNotContain("default value");
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
            && d.Message.Contains("calendar duration", StringComparison.OrdinalIgnoreCase))).IsTrue();
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
        var coreOnly = SessionBuilder.CreateEmpty().LoadCore().Build();
        var pass = new DomainExpressionLoweringPass(
            LoweringFixtures.MinimalContext(new Parameter("entity"), coreOnly));
        await Assert.That(() => pass.Lower(new Duration(12, DurationUnit.Days), new ParameterReference()))
            .Throws<NotSupportedException>();
    }

    [Test]
    public async Task DatePlusHours_ReportsClockResolutionError() {
        var result = Evolve("""
            domain T
            uses temporal
            Item: entity {
              Started: Date
              Bad: policy { Started + 2 Hours > Started }
            }
            """, ExtensionCatalog.Core.Language);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(Errors(result).Any(e =>
            e.Contains("clock-resolution", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task DateTimePlusHours_Succeeds() {
        var result = Evolve("""
            domain T
            uses temporal
            Item: entity {
              OpenedAt: DateTime
              Soon: policy { OpenedAt + 2 Hours > OpenedAt }
            }
            """, ExtensionCatalog.Core.Language);

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Assign_DateFromTime_ConstructedIr_ReportsTypeMismatch() {
        var analysis = AnalyzeAssign("Date", "Time");
        await Assert.That(HasAssignMismatch(analysis, "Target")).IsTrue();
    }

    [Test]
    public async Task Assign_TimeFromDate_ConstructedIr_ReportsTypeMismatch() {
        var analysis = AnalyzeAssign("Time", "Date");
        await Assert.That(HasAssignMismatch(analysis, "Target")).IsTrue();
    }

    [Test]
    public async Task Assign_DateFromDuration_ConstructedIr_ReportsTypeMismatch() {
        var analysis = AnalyzeAssign("Date", "Duration");
        await Assert.That(HasAssignMismatch(analysis, "Target")).IsTrue();
    }

    [Test]
    public async Task Assign_TimeFromTime_ConstructedIr_Succeeds() {
        var analysis = AnalyzeAssign("Time", "Time");
        await Assert.That(analysis.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)).IsFalse();
    }

    [Test]
    public async Task Assign_DateTimeFromDate_ConstructedIr_Succeeds() {
        var analysis = AnalyzeAssign("DateTime", "Date");
        await Assert.That(analysis.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)).IsFalse();
    }

    [Test]
    public async Task Assign_DateFromDateTime_ConstructedIr_ReportsTypeMismatch() {
        var analysis = AnalyzeAssign("Date", "DateTime");
        await Assert.That(HasAssignMismatch(analysis, "Target")).IsTrue();
    }

    [Test]
    public async Task TimePlusHours_ConstructedIr_Succeeds() {
        var item = new Entity("Item",
            [
                new Property("Opens", new DomainTypeReference("Time"), []),
            ],
            [],
            [new Policy("Later", DomainExpression.GreaterThan(
                new DateOperation(
                    DomainExpression.Property("Opens"),
                    DomainExpression.Literal(2L, new DomainTypeReference("Number")),
                    DateOperationKind.AddHours),
                DomainExpression.Property("Opens")))],
            []);
        var analysis = DomainModelAnalyzer.Analyze(
            DomainTestFactory.Create("T", [
                new PrimitiveType("Time", PrimitiveSemantics.Time, []),
                item,
            ], []) with { Extensions = [ExtensionCatalog.TemporalId] });

        await Assert.That(analysis.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)).IsFalse();
    }

    [Test]
    public async Task Now_WithoutTemporalPack_PolicyReference_ReportsUnknownProperty() {
        var raw = SessionBuilder.CreateEmpty().LoadCore().Build();
        var result = Evolve("""
            domain T
            Item: entity {
              Expiry: Text
              IsExpired: policy { Expiry < Now }
            }
            """, raw);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(Errors(result).Any(e =>
            e.Contains("'Now'", StringComparison.Ordinal)
            && e.Contains("does not exist", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task Now_WithoutTemporalPack_ExpressionLowersAsPropertyAccess_NotClock() {
        var raw = SessionBuilder.CreateEmpty().LoadCore().Build();
        var expr = DslExpressionFragment.Resolve(DslExpressionFragment.ParseExpressionFragment("Now"), raw);
        await Assert.That(expr).IsTypeOf<PropertyAccess>();

        var lowered = new DomainExpressionLoweringPass(
                LoweringFixtures.MinimalContext(new Parameter("entity"), raw))
            .Lower(expr, new ParameterReference());
        await Assert.That(lowered).IsTypeOf<Member>();
        var member = (Member)lowered;
        await Assert.That(member.MemberName).IsEqualTo("Now");
        await Assert.That(member.MemberName).IsNotEqualTo("UtcNow");
    }

    [Test]
    public async Task Duration_WithoutTemporalPack_ParseFailsClosed() {
        var raw = SessionBuilder.CreateEmpty().LoadCore().Build();
        await Assert.That(() => DslExpressionFragment.Resolve(
                DslExpressionFragment.ParseExpressionFragment("5 Days"), raw))
            .Throws<FormatException>()
            .WithMessageContaining("requires uses temporal");
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