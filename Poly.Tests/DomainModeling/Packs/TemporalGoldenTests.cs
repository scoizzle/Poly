using Poly.DomainModeling;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Grammar;

namespace Poly.Tests.DomainModeling.Packs;

/// <summary>
/// p1-5: design-lock appendix product goldens as real end-to-end tests.
/// Full .poly apply → stored IR → lowering for the locked temporal vertical:
///
///  - <c>assign DueDate to Now - 12 Days</c> stores
///    <c>DateOperation(Now, Literal(-12), AddDays)</c> and lowers to
///    <c>DateTime.UtcNow.AddDays(-12)</c> (design-lock Q2/Q3).
///  - <c>policy { ExpiryDate &lt; Now }</c> parses to a <c>Comparison</c> against a
///    clock <c>Now</c> node (design-lock Q2).
///  - Unknown unit (<c>12 fortnights</c>) and pack-absent sessions fail closed
///    end-to-end (design-lock Q5 negatives).
///
/// Runtime eval uses the VM clock members (`DateTime.UtcNow` /
/// `DateOnly.FromDateTime`); injectable `TimeProvider` is not a product seam.
///
/// No production edits: everything here exercises the shipped surface.
/// </summary>
public sealed class TemporalGoldenTests {
    private static DomainSession TemporalInputs() =>
        ExtensionCatalog.Core.Language;

    private static Domain Apply(string poly, DomainSession inputs) {
        var changes = new PolyDslParser(poly, inputs).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Apply of temporal domain failed: {result.FailureSummary}");
        return result.Root!;
    }

    private static Entity SingleEntity(Domain domain) =>
        domain.Types.OfType<Entity>().Single();

    private static AssignEffect SingleAssign(Entity entity) {
        var assign = entity.Actions.Single().Effects.OfType<AssignEffect>().SingleOrDefault();
        return assign ?? throw new InvalidOperationException(
            $"Action '{entity.Actions.Single().Name}' has no single AssignEffect.");
    }

    // ── Golden 1: assign DueDate to Now - 12 Days ────────────────────

    private const string RenewDomain = """
        domain TemporalRenew

        Loan: entity {
          DueDate: DateTime
          Renew: action {
            assign DueDate to Now - 12 Days
          }
        }
        """;

    [Test]
    public async Task Now_Minus_12Days_AssignsToDateProperty() {
        var domain = Apply(RenewDomain, TemporalInputs());
        var assign = SingleAssign(SingleEntity(domain));

        await Assert.That(assign.Target).IsTypeOf<PropertyAccess>();
        await Assert.That(((PropertyAccess)assign.Target).Name).IsEqualTo("DueDate");

        await Assert.That(assign.Value).IsTypeOf<DateOperation>();
        var dateOp = (DateOperation)assign.Value;
        await Assert.That(dateOp.Date).IsTypeOf<Now>();
        await Assert.That(dateOp.Kind).IsEqualTo(DateOperationKind.AddDays);
        await Assert.That(dateOp.Offset).IsTypeOf<Literal>();
        await Assert.That(((Literal)dateOp.Offset).Value).IsEqualTo(-12L);
    }

    [Test]
    public async Task Now_Minus_12Days_LowersToUtcNowAddDays_Negative12() {
        var domain = Apply(RenewDomain, TemporalInputs());
        var assign = SingleAssign(SingleEntity(domain));

        var pass = new DomainExpressionLoweringPass(
            LoweringFixtures.MinimalContext(new Parameter("entity")) with { Meaning = TemporalInputs().Meaning });
        var lowered = pass.Lower(assign.Value, new ParameterReference());

        await Assert.That(lowered).IsTypeOf<Invoke>();
        var invoke = (Invoke)lowered;
        await Assert.That(invoke.Delegate).IsTypeOf<Member>();
        var delegateMember = (Member)invoke.Delegate;
        await Assert.That(delegateMember.MemberName).IsEqualTo("AddDays");
        await Assert.That(delegateMember.Value).IsTypeOf<Member>();
        var clockMember = (Member)delegateMember.Value;
        await Assert.That(clockMember.MemberName).IsEqualTo("UtcNow");
        await Assert.That(clockMember.Value).IsTypeOf<NamedTypeReference>();
        await Assert.That(((NamedTypeReference)clockMember.Value).TypeName).IsEqualTo("DateTime");
        await Assert.That(invoke.Arguments.Length).IsEqualTo(1);
        await Assert.That(invoke.Arguments[0]).IsTypeOf<Constant>();
        await Assert.That(((Constant)invoke.Arguments[0]).Value).IsEqualTo(-12L);
    }

    // ── Golden 2: policy { ExpiryDate < Now } ─────────────────────────

    private const string ExpiryDomain = """
        domain TemporalExpiry

        Loan: entity {
          ExpiryDate: DateTime
          IsExpired: policy { ExpiryDate < Now }
        }
        """;

    [Test]
    public async Task ExpiryDate_LessThan_Now_Policy_ParsesToComparison() {
        var domain = Apply(ExpiryDomain, TemporalInputs());
        var policy = SingleEntity(domain).Policies.Single(p => p.Name == "IsExpired");

        await Assert.That(policy.Expression).IsTypeOf<Comparison>();
        var cmp = (Comparison)policy.Expression;
        await Assert.That(cmp.Kind).IsEqualTo(ComparisonKind.LessThan);
        await Assert.That(cmp.Left).IsTypeOf<PropertyAccess>();
        await Assert.That(((PropertyAccess)cmp.Left).Name).IsEqualTo("ExpiryDate");
        await Assert.That(cmp.Right).IsTypeOf<Now>();
    }

    // ── Golden 3 (design-lock negatives, re-asserted e2e) ────────────

    private const string UnknownUnitDomain = """
        domain TemporalUnknownUnit

        Loan: entity {
          DueDate: Date
          Renew: action {
            assign DueDate to Now - 12 Fortnights
          }
        }
        """;

    [Test]
    public async Task UnknownUnit_Fortnights_InAssignRhs_FailsClosedAtParse() {
        await Assert.That(() => Apply(UnknownUnitDomain, TemporalInputs()))
            .Throws<FormatException>();
    }

    // ── G-20: the document parse and Resolve ask one operator-table fold ──

    private static DomainExpression DocumentPath(string text) {
        var session = TemporalInputs();
        var reader = new DslTokenReader(text);
        var cursor = new DslCursor(reader, new Matcher<DslToken, DslTokenKind>(session.Language.Grammar, reader));
        var parsed = new DslExpressionParser(cursor, session.ExpressionForms, session.Folds).ParseExpression();
        return DslExpressionFragment.RejectUnresolvedDuration(parsed);
    }

    private static DomainExpression FragmentPath(string text) =>
        DslExpressionFragment.Resolve(DslExpressionFragment.ParseExpressionFragment(text), TemporalInputs());

    private static async Task AssertDateOp(DomainExpression expr, DateOperationKind kind, long offset) {
        await Assert.That(expr).IsTypeOf<DateOperation>();
        var op = (DateOperation)expr;
        await Assert.That(op.Kind).IsEqualTo(kind);
        await Assert.That(((Literal)op.Offset).Value).IsEqualTo(offset);
    }

    [Test]
    public async Task BothPaths_NowPlus2HoursPlus3Minutes_NestDateOperations() {
        foreach (var expr in new[] { DocumentPath("Now + 2 Hours + 3 Minutes"), FragmentPath("Now + 2 Hours + 3 Minutes") }) {
            await AssertDateOp(expr, DateOperationKind.AddMinutes, 3L);
            var inner = ((DateOperation)expr).Date;
            await AssertDateOp(inner, DateOperationKind.AddHours, 2L);
            await Assert.That(((DateOperation)inner).Date).IsTypeOf<Now>();
        }
    }

    [Test]
    public async Task BothPaths_NowMinus12Days_NegatesTheOffset() {
        foreach (var expr in new[] { DocumentPath("Now - 12 Days"), FragmentPath("Now - 12 Days") }) {
            await AssertDateOp(expr, DateOperationKind.AddDays, -12L);
            await Assert.That(((DateOperation)expr).Date).IsTypeOf<Now>();
        }
    }

    [Test]
    public async Task BothPaths_TodayPlus1Year_IsAddYearsOnToday() {
        foreach (var expr in new[] { DocumentPath("Today + 1 Year"), FragmentPath("Today + 1 Year") }) {
            await AssertDateOp(expr, DateOperationKind.AddYears, 1L);
            await Assert.That(((DateOperation)expr).Date).IsTypeOf<Today>();
        }
    }

    [Test]
    public async Task BothPaths_NowPlusParenthesisedDurationSum_StaysAdd() {
        foreach (var expr in new[] { DocumentPath("Now + (2 Hours + 3 Minutes)"), FragmentPath("Now + (2 Hours + 3 Minutes)") }) {
            await Assert.That(expr).IsTypeOf<Poly.DomainModeling.Ontology.Add>();
            var add = (Poly.DomainModeling.Ontology.Add)expr;
            await Assert.That(add.Left).IsTypeOf<Now>();
            await Assert.That(add.Right).IsTypeOf<Poly.DomainModeling.Ontology.Add>();
            var sum = (Poly.DomainModeling.Ontology.Add)add.Right;
            await Assert.That(((Duration)sum.Left).Amount).IsEqualTo(2L);
            await Assert.That(((Duration)sum.Left).Unit).IsEqualTo(DurationUnit.Hours);
            await Assert.That(((Duration)sum.Right).Amount).IsEqualTo(3L);
            await Assert.That(((Duration)sum.Right).Unit).IsEqualTo(DurationUnit.Minutes);
        }
    }

    [Test]
    public async Task BothPaths_DurationPlusNow_StaysAdd() {
        foreach (var expr in new[] { DocumentPath("3 Days + Now"), FragmentPath("3 Days + Now") }) {
            await Assert.That(expr).IsTypeOf<Poly.DomainModeling.Ontology.Add>();
            var add = (Poly.DomainModeling.Ontology.Add)expr;
            await Assert.That(((Duration)add.Left).Amount).IsEqualTo(3L);
            await Assert.That(add.Right).IsTypeOf<Now>();
        }
    }

    [Test]
    public async Task BothPaths_PropertyPlusNumber_StaysAdd() {
        foreach (var text in new[] { "Age + 1", "Now + 1" }) {
            foreach (var expr in new[] { DocumentPath(text), FragmentPath(text) }) {
                await Assert.That(expr).IsTypeOf<Poly.DomainModeling.Ontology.Add>();
                var add = (Poly.DomainModeling.Ontology.Add)expr;
                await Assert.That(((Literal)add.Right).Value).IsEqualTo(1L);
            }
        }
    }

    /// <summary>The fold is type-blind by design (the parser has no types): analysis rejects a non-date left operand.</summary>
    [Test]
    public async Task BothPaths_PricePlus3Days_FoldsTypeBlind_AndAnalysisRejects() {
        foreach (var expr in new[] { DocumentPath("Price + 3 Days"), FragmentPath("Price + 3 Days") }) {
            await AssertDateOp(expr, DateOperationKind.AddDays, 3L);
            await Assert.That(((PropertyAccess)((DateOperation)expr).Date).Name).IsEqualTo("Price");
        }

        var ex = Assert.Throws<InvalidOperationException>(() => Apply("""
            domain T
            uses temporal
            Item: entity {
              Price: Number
              Expiry: Date
              Bad: policy { Price + 3 Days > Expiry }
            }
            """, TemporalInputs()));
        await Assert.That(ex.Message).Contains("date left operand");
    }

    /// <summary>A node as text, by its public properties, without ids, positions or the children list.</summary>
    private static string Dump(object? node) {
        switch (node) {
            case null:
                return "null";
            case Node:
                var parts = node.GetType().GetProperties()
                    .Where(p => p.Name is not ("Id" or "Children" or "Line" or "Col" or "EqualityContract") && p.GetIndexParameters().Length == 0)
                    .Select(p => $"{p.Name}={Dump(p.GetValue(node))}");
                return $"{node.GetType().Name}({string.Join(", ", parts)})";
            case string text:
                return text;
            case System.Collections.IEnumerable items:
                return "[" + string.Join(", ", items.Cast<object?>().Select(Dump)) + "]";
            default:
                return node.ToString()!;
        }
    }

    /// <summary>
    /// Whole-tree pins, recorded on master before the bridge was retired and identical on both paths. <c>Now + 2 Days * 3</c>
    /// stays <c>Add(Now, Multiply(2 Days, 3))</c>: the higher row groups first and nothing is claimed.
    /// </summary>
    [Test]
    [Arguments("Now + 2 Days * 3", "Add(Left=Now(), Right=Multiply(Left=Duration(Amount=2, Unit=Days), Right=Literal(Value=3, TypeHint=null)))")]
    [Arguments("Now - 1 Days + 2 Days", "DateOperation(Date=DateOperation(Date=Now(), Offset=Literal(Value=-1, TypeHint=DomainTypeReference(TypeName=Number)), Kind=AddDays), Offset=Literal(Value=2, TypeHint=DomainTypeReference(TypeName=Number)), Kind=AddDays)")]
    [Arguments("Now + 1 Days > Today", "Comparison(Left=DateOperation(Date=Now(), Offset=Literal(Value=1, TypeHint=DomainTypeReference(TypeName=Number)), Kind=AddDays), Right=Today(), Kind=GreaterThan)")]
    [Arguments("any Loans where Created + 1 Days > Now", "AnyExpr(RelationshipName=Loans, Body=Comparison(Left=DateOperation(Date=PropertyAccess(Name=Created), Offset=Literal(Value=1, TypeHint=DomainTypeReference(TypeName=Number)), Kind=AddDays), Right=Now(), Kind=GreaterThan))")]
    [Arguments("(Start + 1 Days) + 2 Days", "DateOperation(Date=DateOperation(Date=PropertyAccess(Name=Start), Offset=Literal(Value=1, TypeHint=DomainTypeReference(TypeName=Number)), Kind=AddDays), Offset=Literal(Value=2, TypeHint=DomainTypeReference(TypeName=Number)), Kind=AddDays)")]
    public async Task BothPaths_WholeTree_IsIdentical_AndPinned(string text, string expected) {
        var document = Dump(DocumentPath(text));
        var fragment = Dump(FragmentPath(text));

        await Assert.That(fragment).IsEqualTo(document);
        await Assert.That(document).IsEqualTo(expected);
    }

    [Test]
    public async Task BothPaths_NowMinus12Fortnights_ThrowsFormatException() {
        await Assert.That(() => DocumentPath("Now - 12 Fortnights")).Throws<FormatException>();
        await Assert.That(() => FragmentPath("Now - 12 Fortnights")).Throws<FormatException>();
    }

    private const string PackAbsentDomain = """
        domain TemporalPackAbsent

        Loan: entity {
          DueDate: Date
          Renew: action {
            assign DueDate to Now - 12 Days
          }
        }
        """;

    [Test]
    public async Task SessionWithoutTemporalPack_AssignNowMinusDays_RejectsAuthoring() {
        // Raw session never calls LoadTemporal, so Now/duration forms stay unregistered.
        var raw = SessionBuilder.CreateEmpty().LoadCore().Build();
        await Assert.That(() => new PolyDslParser(PackAbsentDomain, raw).Parse())
            .Throws<FormatException>();
    }
}