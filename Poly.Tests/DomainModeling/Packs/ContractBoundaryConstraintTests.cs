using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Constraints;
using Poly.DomainModeling.Ontology.Contract;
using Poly.DomainModeling.Runtime;
using Poly.Tests.TestHelpers;

using static Poly.Tests.TestHelpers.ContractContributions;

using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.Tests.DomainModeling.Packs;

/// <summary>
/// #425 (epic #427, spike #413 decision 21): at a resolved bind, analysis checks the caller's
/// argument against the endpoint's public payload constraints and the preconditions that reduce
/// to a payload-property comparison. Proven lowers; violated is <c>DMCON014</c> and refuses;
/// unknown is <c>DMCON015</c> and does not block. Scot's 2026-11-08 ruling: no library body is
/// walked, and every contributed body is unknown on the postcondition half.
/// </summary>
public sealed class ContractBoundaryConstraintTests {
    private static ImportedContract Surface(
        IReadOnlyList<Constraint>? amountConstraints = null,
        IReadOnlyList<Policy>? preconditions = null,
        ContractSourceKind kind = ContractSourceKind.ExternalProvider) =>
        new(
            "Billing",
            kind,
            "billing",
            "v1",
            [
                new ContractEndpoint(
                    "Charge",
                    ContractEndpointKind.Operation,
                    ContractEndpointDirection.Outbound,
                    new DomainTypeReference("ChargeRequest")) {
                    SourceName = "Charge",
                    Preconditions = preconditions ?? [],
                },
            ]) {
            Types = [
                new ValueType("ChargeRequest", [
                    new Property("Amount", new DomainTypeReference("Number"), amountConstraints ?? []),
                ], []),
            ],
        };

    private static (Domain Domain, DomainSession Session, AnalysisResult Analysis) Analyze(
        string poly,
        ImportedContract surface,
        IReadOnlyDictionary<string, Func<Poly.Ast.Nodes.Block>>? bodies = null) {
        var tree = DslSyntaxTree.Parse(poly);
        var session = DomainSession.ForDocument(tree, [], Catalog(new ContractLibrary("billing", surface, bodies)));
        var changes = new DomainResolver(tree, session).Resolve();
        // Apply without the analysis gate: a test that expects an error still needs the
        // proposed root (and its facts), not the rolled-back seed.
        var applied = new DomainEvolution(new Domain("_", [])).Apply(changes, session: session);
        var analysis = session.Analyze(applied.Root);
        return (applied.Root, session, analysis);
    }

    private static IReadOnlyList<Diagnostic> WithCode(AnalysisResult analysis, string code) =>
        analysis.Diagnostics.Where(d => string.Equals(d.Code, code, StringComparison.Ordinal)).ToList();

    private static ContractBoundaryMetadata Boundary(Domain domain, AnalysisResult analysis) =>
        analysis.GetMetadata<ContractBoundaryMetadata>(domain.ContractBindings.Single())
        ?? throw new InvalidOperationException("No boundary facts were recorded.");

    private static string CallerDsl(string amountConstraints) => $$"""
        domain Shop
        uses billing

        SmallRequest: value {
          Amount: Number {{amountConstraints}}
        }

        Order: entity {
          Pay: action (request: SmallRequest) { }
        }

        ChargeOrder: bind Billing Charge to Pay request
        """;

    private static Policy AmountPositive() => new(
        "Positive",
        DomainExpression.GreaterThan(DomainExpression.Property("Amount"), DomainExpression.Literal(0)));

    [Test]
    public async Task CallerNarrowerThanPayload_IsProvenAndLowers() {
        var (domain, session, analysis) = Analyze(
            CallerDsl("range(0, 50)"),
            Surface(amountConstraints: [new RangeConstraint(0, 100)], kind: ContractSourceKind.InternalDomain));

        await Assert.That(analysis.HasErrors).IsFalse();
        await Assert.That(WithCode(analysis, "DMCON014")).IsEmpty();
        var boundary = Boundary(domain, analysis);
        await Assert.That(boundary.Precondition).IsEqualTo(ContractBoundaryVerdict.Proven);
        await Assert.That(boundary.Compatibility).IsEqualTo(ContractCompatibility.Satisfies);
        await Assert.That(session.Lower(domain, analysis).Count).IsGreaterThan(0);
    }

    [Test]
    public async Task CallerDisjointFromPayload_IsViolatedAndRefusesLower() {
        var (domain, session, analysis) = Analyze(
            CallerDsl("range(20, 30)"), Surface(amountConstraints: [new RangeConstraint(0, 10)]));

        var finding = WithCode(analysis, "DMCON014").Single();
        await Assert.That(finding.Message).Contains("range");
        await Assert.That(Boundary(domain, analysis).Precondition)
            .IsEqualTo(ContractBoundaryVerdict.Violated);
        await Assert.That(() => session.Lower(domain, analysis)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task CallerWiderThanPayload_IsUnknownAndDoesNotBlockLower() {
        var (domain, session, analysis) = Analyze(
            CallerDsl(""),
            Surface(amountConstraints: [new RangeConstraint(0, 100)], kind: ContractSourceKind.InternalDomain));

        await Assert.That(analysis.HasErrors).IsFalse();
        await Assert.That(WithCode(analysis, "DMCON014")).IsEmpty();
        await Assert.That(WithCode(analysis, "DMCON015")).IsNotEmpty();
        await Assert.That(Boundary(domain, analysis).Precondition)
            .IsEqualTo(ContractBoundaryVerdict.Unknown);
        await Assert.That(session.Lower(domain, analysis).Count).IsGreaterThan(0);
    }

    /// <summary>
    /// #442: a constraint pair the merge cannot decide is unknown, never a violation. An
    /// equality cannot be shown to sit inside a range by the merge table, so it is unknown.
    /// </summary>
    [Test]
    public async Task CannotDecide_IsUnknownNotViolated() {
        var (domain, _, analysis) = Analyze(
            CallerDsl("equals(5)"), Surface(amountConstraints: [new RangeConstraint(0, 10)]));

        await Assert.That(WithCode(analysis, "DMCON014")).IsEmpty();
        await Assert.That(WithCode(analysis, "DMCON015")).IsNotEmpty();
        await Assert.That(Boundary(domain, analysis).Precondition)
            .IsEqualTo(ContractBoundaryVerdict.Unknown);
    }

    [Test]
    public async Task CallerSatisfyingPayloadPrecondition_IsProven() {
        var (domain, _, analysis) = Analyze(
            CallerDsl("range(1, 10)"), Surface(preconditions: [AmountPositive()]));

        await Assert.That(WithCode(analysis, "DMCON014")).IsEmpty();
        await Assert.That(Boundary(domain, analysis).Precondition)
            .IsEqualTo(ContractBoundaryVerdict.Proven);
    }

    [Test]
    public async Task CallerViolatingPayloadPrecondition_IsViolatedAndNamesIt() {
        var (domain, _, analysis) = Analyze(
            CallerDsl("range(-5, -1)"), Surface(preconditions: [AmountPositive()]));

        var finding = WithCode(analysis, "DMCON014").Single();
        await Assert.That(finding.Message).Contains("Positive");
        await Assert.That(Boundary(domain, analysis).Precondition)
            .IsEqualTo(ContractBoundaryVerdict.Violated);
    }

    [Test]
    public async Task PreconditionOnAPropertyTheArgumentDoesNotCarry_IsUnknown() {
        var lowQty = new Policy(
            "LowQty",
            DomainExpression.LessThanOrEqual(DomainExpression.Property("Total"), DomainExpression.Literal(80)));
        var (domain, _, analysis) = Analyze(
            CallerDsl("range(0, 10)"), Surface(preconditions: [lowQty]));

        await Assert.That(WithCode(analysis, "DMCON014")).IsEmpty();
        var warning = WithCode(analysis, "DMCON015").Single();
        await Assert.That(warning.Message).Contains("LowQty");
        await Assert.That(Boundary(domain, analysis).Precondition)
            .IsEqualTo(ContractBoundaryVerdict.Unknown);
        await Assert.That(Boundary(domain, analysis).Compatibility)
            .IsEqualTo(ContractCompatibility.Incomparable);
    }

    /// <summary>
    /// Scot 2026-11-08: a contributed body is not walked. Its postcondition half is unknown,
    /// named once, and Lower still runs.
    /// </summary>
    [Test]
    public async Task ContributedBody_IsUnknownOnThePostconditionHalfAndLowers() {
        var bodies = new Dictionary<string, Func<Poly.Ast.Nodes.Block>>(StringComparer.Ordinal) {
            ["Charge"] = TraceBody("Billing.Charge"),
        };
        var (domain, session, analysis) = Analyze(
            CallerDsl("range(0, 50)"),
            Surface(amountConstraints: [new RangeConstraint(0, 100)]),
            bodies);

        await Assert.That(analysis.HasErrors).IsFalse();
        var boundary = Boundary(domain, analysis);
        await Assert.That(boundary.Precondition).IsEqualTo(ContractBoundaryVerdict.Proven);
        await Assert.That(boundary.Postcondition).IsEqualTo(ContractBoundaryVerdict.Unknown);
        var warning = WithCode(analysis, "DMCON015").Single();
        await Assert.That(warning.Message).Contains("Charge");
        await Assert.That(session.Emit(domain, analysis).Count).IsGreaterThan(0);
    }

    [Test]
    public async Task StaleFingerprintPin_StaysDmcon010AndTheNoteDoesNotClearIt() {
        var stale = new string('0', 64);
        var poly = $$"""
            domain Shop
            uses billing {{stale}}

            SmallRequest: value {
              Amount: Number range(0, 50)
            }

            Order: entity {
              Pay: action (request: SmallRequest) { }
            }

            ChargeOrder: bind Billing Charge to Pay request
            """;
        var (domain, _, analysis) = Analyze(poly, Surface(amountConstraints: [new RangeConstraint(0, 100)]));

        await Assert.That(WithCode(analysis, "DMCON010")).IsNotEmpty();
        await Assert.That(analysis.HasErrors).IsTrue();
        await Assert.That(Boundary(domain, analysis).Precondition)
            .IsEqualTo(ContractBoundaryVerdict.Proven);
    }
}