using Poly.Analysis;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Libraries.Platform;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Constraints;
using Poly.DomainModeling.Ontology.Contract;
using Poly.Tests.TestHelpers;

using static Poly.Tests.TestHelpers.ContractContributions;

using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.Tests.DomainModeling.Packs;

/// <summary>
/// #422 (epic #427, spike #413 decision 8): a contract failure is acknowledged by the existing
/// <see cref="DiagnosticAllowance"/>. A match reports the finding as a warning, stores one
/// queryable <see cref="ContractToleranceMetadata"/> fact on the domain, and still refuses
/// Lower, Emit, Compile, and direct projection by naming that fact. Strict analysis is unchanged;
/// flow findings and the never-acknowledgeable codes are not routed through the contract arm.
/// </summary>
public sealed class ContractToleranceTests {
    private static readonly string[] ProvenanceIds = ["plsql-1", "csharp-2"];

    private const string MissingEndpointDsl = """
        domain Shop
        uses billing

        Order: entity { Pay: action (request: ChargeRequest) { } }
        ChargeOrder: bind Billing Nope to Pay request
        """;

    private const string CleanBindDsl = """
        domain Shop
        uses billing

        Order: entity { Pay: action (request: ChargeRequest) { } }
        ChargeOrder: bind Billing Charge to Pay request
        """;

    private static DiagnosticAllowance Allow(string code, string path, params string[] ids) =>
        new(code, path, ids.Length == 0 ? ProvenanceIds : ids);

    private static IDomainLibrary Billing(ImportedContract? surface = null) =>
        new ContractLibrary("billing", surface ?? BillingSurface());

    private static (Domain Domain, DomainSession Session, AnalysisResult Analysis) Analyze(
        string poly,
        IDomainLibrary? library = null,
        IReadOnlyList<DiagnosticAllowance>? allowances = null,
        string? target = null,
        IReadOnlyList<ObservedFlow>? flows = null) {
        var tree = DslSyntaxTree.Parse(poly);
        var builder = SessionBuilder.CreateEmpty().LoadCore().LoadTemporal();
        if (library is not null)
            builder.Load(library);
        var session = builder
            .WithObservedFlows(flows)
            .WithDiagnosticAllowances(allowances)
            .Build()
            .WithTarget(target);
        var applied = new DomainEvolution(new Domain("_", []))
            .Apply(new DomainResolver(tree, session).Resolve(), session: session);
        return (applied.Root, session, session.Analyze(applied.Root));
    }

    private static Diagnostic Single(AnalysisResult analysis, string code) =>
        analysis.Diagnostics.Single(finding => string.Equals(finding.Code, code, StringComparison.Ordinal));

    private static ContractToleranceMetadata? Tolerance(Domain domain, AnalysisResult analysis) =>
        analysis.GetMetadata<ContractToleranceMetadata>(domain);

    private static ImportedContract OverloadSurface(
        IReadOnlyList<(string Name, string Source, string Payload)> endpoints) =>
        new(
            "Billing",
            ContractSourceKind.ExternalProvider,
            "billing",
            "v1",
            endpoints.Select(endpoint => new ContractEndpoint(
                endpoint.Name,
                ContractEndpointKind.Operation,
                ContractEndpointDirection.Outbound,
                new DomainTypeReference(endpoint.Payload)) {
                SourceName = endpoint.Source,
            }).ToList()) {
            Types = [
                new ValueType("Money",
                    [new Property("Value", new DomainTypeReference("Number"), [new RangeConstraint(0, 100)])], []),
                new ValueType("Small",
                    [new Property("Value", new DomainTypeReference("Number"), [new RangeConstraint(0, 1000)])], []),
                new ValueType("Wide",
                    [new Property("Value", new DomainTypeReference("Number"), [new RangeConstraint(-100, 100)])], []),
            ],
        };

    [Test]
    public async Task Strict_UnresolvedEndpoint_IsAnErrorWithNoFact_AndLowerThrowsTheFinding() {
        var (domain, session, analysis) = Analyze(MissingEndpointDsl, Billing());

        var finding = Single(analysis, DomainModelDiagnosticCodes.ContractEndpointNotOnSurface);
        await Assert.That(finding.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(Tolerance(domain, analysis) is null).IsTrue();

        var refused = Assert.Throws<InvalidOperationException>(() => session.Lower(domain, analysis));
        await Assert.That(refused.Message).IsEqualTo(finding.Message);
    }

    [Test]
    public async Task Tolerant_UnresolvedEndpoint_IsAWarningWithAFact_AndLowerStillRefuses() {
        var (domain, session, analysis) = Analyze(
            MissingEndpointDsl, Billing(), [Allow("DMCON013", "Shop/ChargeOrder")]);

        await Assert.That(analysis.HasErrors).IsFalse();
        var finding = Single(analysis, DomainModelDiagnosticCodes.ContractEndpointNotOnSurface);
        await Assert.That(finding.Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(analysis.Diagnostics.Any(d => d.Code == DomainModelDiagnosticCodes.UnusedDiagnosticAllowance)).IsFalse();

        var fact = Tolerance(domain, analysis)!.Facts.Single();
        await Assert.That(fact.Code).IsEqualTo("DMCON013");
        await Assert.That(fact.ElementPath).IsEqualTo("Shop/ChargeOrder");

        var refused = Assert.Throws<InvalidOperationException>(() => session.Lower(domain, analysis));
        await Assert.That(refused.Message).Contains("DMCON013").And.Contains("Shop/ChargeOrder");
    }

    [Test]
    public async Task Tolerant_AnalysisIsNonFatal_WhileLowerRefuses() {
        var tree = DslSyntaxTree.Parse(MissingEndpointDsl);
        var session = SessionBuilder.CreateEmpty().LoadCore().LoadTemporal()
            .Load(Billing())
            .WithDiagnosticAllowances([Allow("DMCON013", "Shop/ChargeOrder")])
            .Build();
        var result = new DomainEvolution(new Domain("_", []))
            .ApplyAndAnalyze(new DomainResolver(tree, session).Resolve(), session: session);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Analysis.HasErrors).IsFalse();
        Assert.Throws<InvalidOperationException>(() => session.Lower(result.Root!, result.Analysis));
    }

    [Test]
    public async Task Tolerant_StalePin_IsAWarningWithAFact_AndLowerRefuses() {
        const string poly = """
            domain Shop
            uses billing v2

            Order: entity { Total: Number }
            """;
        var (domain, session, analysis) = Analyze(poly, Billing(), [Allow("DMCON010", "Shop/Billing")]);

        await Assert.That(analysis.HasErrors).IsFalse();
        await Assert.That(Single(analysis, DomainModelDiagnosticCodes.ContractPinMismatch).Severity)
            .IsEqualTo(DiagnosticSeverity.Warning);
        var surface = analysis.GetMetadata<ContractSurfaceMetadata>(domain.ImportedContracts.Single())!;
        await Assert.That(surface.PinMatched).IsFalse();
        await Assert.That(Tolerance(domain, analysis)!.Facts.Single().Code).IsEqualTo("DMCON010");

        Assert.Throws<InvalidOperationException>(() => session.Lower(domain, analysis));
    }

    [Test]
    public async Task Tolerant_UnregisteredContract_IsAWarningWithAFact_AndLowerRefuses() {
        const string poly = """
            domain Shop

            Order: entity { Pay: action (request: Text) { } }
            ChargeOrder: bind Missing Charge to Pay request
            """;
        var (domain, session, analysis) = Analyze(poly, allowances: [Allow("DMCON005", "Shop/ChargeOrder")]);

        await Assert.That(analysis.HasErrors).IsFalse();
        await Assert.That(Single(analysis, DomainModelDiagnosticCodes.ContractReferenceUnresolved).Severity)
            .IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(Tolerance(domain, analysis)!.Facts.Single().Code).IsEqualTo("DMCON005");
        Assert.Throws<InvalidOperationException>(() => session.Lower(domain, analysis));
    }

    [Test]
    public async Task Tolerant_PlatformTargetMismatch_IsAWarningWithAFact_AndLowerRefuses() {
        const string poly = """
            domain Shop
            uses posix

            Order: entity { Total: Number }
            """;
        var (domain, session, analysis) = Analyze(
            poly, new PosixLibrary(), [Allow("DMCON004", "Shop/Posix")], target: "windows");

        await Assert.That(analysis.HasErrors).IsFalse();
        await Assert.That(Single(analysis, DomainModelDiagnosticCodes.PlatformTargetMismatch).Severity)
            .IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(Tolerance(domain, analysis)!.Facts.Single().ElementPath).IsEqualTo("Shop/Posix");
        Assert.Throws<InvalidOperationException>(() => session.Lower(domain, analysis));
    }

    [Test]
    public async Task Tolerant_UnclosedPublicSignature_DedupesFactsPerElementPath() {
        const string poly = """
            domain Shop

            Money: value { Amount: Number }

            public entity Order {
              public action Pay (first: Money, second: Money) {
              }
            }
            """;
        var (domain, session, analysis) = Analyze(poly, allowances: [Allow("DMCON002", "Shop/Order/Pay")]);

        await Assert.That(analysis.HasErrors).IsFalse();
        var findings = analysis.Diagnostics
            .Where(finding => finding.Code == DomainModelDiagnosticCodes.PublicSurfaceNotClosed)
            .ToList();
        await Assert.That(findings.Count).IsEqualTo(2);
        await Assert.That(findings.All(finding => finding.Severity == DiagnosticSeverity.Warning)).IsTrue();
        var facts = Tolerance(domain, analysis)!.Facts;
        await Assert.That(facts.Count).IsEqualTo(1);
        await Assert.That(facts[0].Code).IsEqualTo("DMCON002");
        Assert.Throws<InvalidOperationException>(() => session.Lower(domain, analysis));
    }

    [Test]
    public async Task Tolerant_PublicActionOnInternalEntity_IsAWarningWithAFact_AndLowerRefuses() {
        const string poly = """
            domain Shop

            Order: entity {
              public action Pay {
              }
            }
            """;
        var (domain, session, analysis) = Analyze(poly, allowances: [Allow("DMCON003", "Shop/Order/Pay")]);

        await Assert.That(analysis.HasErrors).IsFalse();
        await Assert.That(Single(analysis, DomainModelDiagnosticCodes.PublicMemberOnInternalOwner).Severity)
            .IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(Tolerance(domain, analysis)!.Facts.Single().Code).IsEqualTo("DMCON003");
        Assert.Throws<InvalidOperationException>(() => session.Lower(domain, analysis));
    }

    [Test]
    public async Task StaleOrAnonymousAllowance_LeavesTheFindingAnError_AndIsDmalw001() {
        foreach (var allowance in new[] {
            Allow("DMCON013", "Shop/Other"),
            Allow("DMCON010", "Shop/ChargeOrder"),
            new DiagnosticAllowance("DMCON013", "Shop/ChargeOrder", []),
        }) {
            var (domain, session, analysis) = Analyze(MissingEndpointDsl, Billing(), [allowance]);

            await Assert.That(Single(analysis, DomainModelDiagnosticCodes.ContractEndpointNotOnSurface).Severity)
                .IsEqualTo(DiagnosticSeverity.Error);
            await Assert.That(Single(analysis, DomainModelDiagnosticCodes.UnusedDiagnosticAllowance).Severity)
                .IsEqualTo(DiagnosticSeverity.Error);
            await Assert.That(Tolerance(domain, analysis) is null).IsTrue();
            Assert.Throws<InvalidOperationException>(() => session.Lower(domain, analysis));
        }
    }

    [Test]
    public async Task NeverAcknowledgeableCodes_StayErrors_AndAreDmalw001() {
        // DMCON012: an ambiguous overload is an authoring error, not a proposal to store.
        var ambiguous = OverloadSurface([
            ("Charge__Small", "Charge", "Small"),
            ("Charge__Wide", "Charge", "Wide"),
        ]);
        const string overloadDsl = """
            domain Shop
            uses billing

            Order: entity { Pay: action (request: Money) { } }
            ChargeOrder: bind Billing Charge to Pay request
            """;
        var (ambiguousDomain, ambiguousSession, ambiguousAnalysis) = Analyze(
            overloadDsl, Billing(ambiguous), [Allow("DMCON012", "Shop/ChargeOrder")]);
        await Assert.That(Single(ambiguousAnalysis, DomainModelDiagnosticCodes.ContractOverloadAmbiguous).Severity)
            .IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(Single(ambiguousAnalysis, DomainModelDiagnosticCodes.UnusedDiagnosticAllowance).Severity)
            .IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(Tolerance(ambiguousDomain, ambiguousAnalysis) is null).IsTrue();
        Assert.Throws<InvalidOperationException>(() => ambiguousSession.Lower(ambiguousDomain, ambiguousAnalysis));

        // DMCON001: package metadata is not an unresolved proposal.
        var badSurface = new ImportedContract("Billing", ContractSourceKind.ExternalProvider, "billing", "", []);
        const string badPackageDsl = """
            domain Shop
            uses billing

            Order: entity { Total: Number }
            """;
        var (badDomain, badSession, badAnalysis) = Analyze(
            badPackageDsl, Billing(badSurface), [Allow("DMCON001", "Shop/Billing")]);
        await Assert.That(Single(badAnalysis, DomainModelDiagnosticCodes.ContractIntegration).Severity)
            .IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(Tolerance(badDomain, badAnalysis) is null).IsTrue();
        await Assert.That(Single(badAnalysis, DomainModelDiagnosticCodes.UnusedDiagnosticAllowance).Severity)
            .IsEqualTo(DiagnosticSeverity.Error);
        Assert.Throws<InvalidOperationException>(() => badSession.Lower(badDomain, badAnalysis));
    }

    [Test]
    public async Task AcknowledgedFlowFinding_StillLowers_AndStoresNoContractFact() {
        const string poly = """
            domain Orders
            Customer: entity {
              Notes: Text length(0, 200)
            }
            Order: entity {
              Notes: Text length(0, 50)
            }
            """;
        var (domain, session, analysis) = Analyze(
            poly,
            allowances: [Allow("DMEFF008", "Orders/Order/Notes")],
            flows: [new ObservedFlow("Customer.Notes", "Order.Notes")]);

        await Assert.That(analysis.HasErrors).IsFalse();
        await Assert.That(Single(analysis, "DMEFF008").Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(Tolerance(domain, analysis) is null).IsTrue();
        await Assert.That(session.Lower(domain, analysis).Count).IsGreaterThan(0);
    }

    [Test]
    public async Task CleanBind_Lowers_AndStoresNoToleranceFact() {
        var (domain, session, analysis) = Analyze(CleanBindDsl, Billing());

        await Assert.That(analysis.HasErrors).IsFalse();
        await Assert.That(Tolerance(domain, analysis) is null).IsTrue();
        await Assert.That(session.Lower(domain, analysis).Count).IsGreaterThan(0);
    }

    [Test]
    public async Task ToleratedContractFailure_RefusesEveryLowerDoor_EvenAfterACachedLowerAttempt() {
        var (domain, session, analysis) = Analyze(
            MissingEndpointDsl, Billing(), [Allow("DMCON013", "Shop/ChargeOrder")]);

        Assert.Throws<InvalidOperationException>(() => session.Lower(domain, analysis));
        Assert.Throws<InvalidOperationException>(() => session.Emit(domain, analysis));
        Assert.Throws<InvalidOperationException>(() => session.Compile(domain, analysis));
        Assert.Throws<InvalidOperationException>(() => DomainProgramProjection.ToSyntax(domain, analysis));
        // No cache escape: the refused Lower above must not have stored a module for the next call.
        Assert.Throws<InvalidOperationException>(() => session.Lower(domain, analysis));
    }

    [Test]
    public async Task NonBoundBindingFact_RefusesInTheSplice_WithoutTheDomainFact() {
        var (domain, _, analysis) = Analyze(MissingEndpointDsl, Billing());
        var binding = domain.ContractBindings.Single();
        await Assert.That(analysis.GetMetadata<ContractBindingMetadata>(binding)!.Status)
            .IsEqualTo(ContractBindingStatus.NotOnSurface);
        await Assert.That(analysis.GetMetadata<ContractToleranceMetadata>(domain) is null).IsTrue();

        var refused = Assert.Throws<InvalidOperationException>(
            () => DomainProgramProjection.ToSyntax(
                domain, (INodeMetadataProvider)analysis, policyBodies: null, subscriptionBodies: null));
        await Assert.That(refused.Message).Contains("NotOnSurface");
    }
}