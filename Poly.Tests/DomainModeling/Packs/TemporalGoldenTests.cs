using Poly.Ast.Nodes;
using Poly.DomainModeling;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.ContractFill;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Libraries.Storage;
using Poly.DomainModeling.Libraries.Temporal;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology.Effects;

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
/// Runtime eval with a fixed clock is intentionally absent: the design-lock
/// store/preprocess clock seam (Q3/Q4 — resolve <c>Now</c> once per eval via
/// injectable <c>TimeProvider</c>) is not implemented, so the VM fails on static
/// clock members (<c>NamedTypeReference</c> in <c>DirectVmAbiEmitter</c>). That is
/// a production blocker, not a test gap.
///
/// No production edits: everything here exercises the shipped surface.
/// </summary>
public sealed class TemporalGoldenTests {
    private static DomainSession TemporalInputs() =>
        ExtensionCatalog.Core.Language;

    private static Domain Apply(string poly, DomainSession inputs) {
        var changes = new PolyDslParser(poly, inputs).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
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
          DueDate: Date
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
            new LoweringContext(new Parameter("entity"), Meaning: ExtensionCatalog.Core.Language.Meaning));
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
          ExpiryDate: Date
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
        // No parser inputs → default (pack-absent) inputs: the pack is the only
        // way to register the Now/duration forms, so temporal authoring must fail.
        await Assert.That(() => new PolyDslParser(PackAbsentDomain).Parse())
            .Throws<FormatException>();
    }
}