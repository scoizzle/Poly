using Poly.Analysis;
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
/// #420 (epic #427, spike #413 decisions 1 and 5): analysis records each bind as a fact
/// (source, version, fingerprint, resolved endpoint, status) and checks the <c>uses</c> pin
/// exactly. A bind may name the original action; the payload constraint subsumption relation
/// picks the more-constrained exported endpoint, and Lower splices the resolved identity.
/// </summary>
public sealed class ContractBindingFactsTests {
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
                new ValueType("Amount",
                    [new Property("Value", new DomainTypeReference("Number"), [])], []),
                new ValueType("Money",
                    [new Property("Value", new DomainTypeReference("Number"), [new RangeConstraint(0, 100)])], []),
                new ValueType("Small",
                    [new Property("Value", new DomainTypeReference("Number"), [new RangeConstraint(0, 1000)])], []),
                new ValueType("Wide",
                    [new Property("Value", new DomainTypeReference("Number"), [new RangeConstraint(-100, 100)])], []),
                new ValueType("TextPayload",
                    [new Property("Value", new DomainTypeReference("Text"), [])], []),
            ],
        };

    private static (Domain Domain, DomainSession Session, AnalysisResult Analysis) Analyze(
        string poly,
        IDomainLibrary library) {
        var tree = DslSyntaxTree.Parse(poly);
        var session = DomainSession.Create(tree.Uses, SessionOptions.Bare(Catalog(library)));
        var changes = new DomainResolver(tree, session).Resolve();
        // Apply without the analysis gate: a test that expects an error still needs the
        // proposed root (and its facts), not the rolled-back seed.
        var applied = new DomainEvolution(new Domain("_", [])).Apply(changes, session: session);
        var analysis = session.Analyze(applied.Root);
        return (applied.Root, session, analysis);
    }

    private static (Domain Domain, DomainSession Session, AnalysisResult Analysis) Analyze(
        string poly,
        ImportedContract surface,
        string libraryId = "billing",
        IReadOnlyDictionary<string, Func<Poly.Ast.Nodes.Block>>? bodies = null) =>
        Analyze(poly, new ContractLibrary(libraryId, surface, bodies));

    private static IReadOnlyList<Diagnostic> WithCode(AnalysisResult analysis, string code) =>
        analysis.Diagnostics.Where(d => string.Equals(d.Code, code, StringComparison.Ordinal)).ToList();

    private static ContractBindingMetadata BindFacts(Domain domain, AnalysisResult analysis) =>
        analysis.GetMetadata<ContractBindingMetadata>(domain.ContractBindings.Single())
        ?? throw new InvalidOperationException("No binding facts were recorded.");

    private const string NoPinDsl = """
        domain Shop
        uses billing

        Order: entity {
          Pay: action (request: ChargeRequest) { }
        }

        ChargeOrder: bind Billing Charge to Pay request
        """;

    [Test]
    public async Task Uses_NoPin_RecordsTheResolvedFingerprintAndLowers() {
        var (domain, session, analysis) = Analyze(NoPinDsl, BillingSurface());
        var contract = domain.ImportedContracts.Single();

        await Assert.That(analysis.HasErrors).IsFalse();
        var surface = analysis.GetMetadata<ContractSurfaceMetadata>(contract)
            ?? throw new InvalidOperationException("No surface facts were recorded.");
        await Assert.That(surface.Fingerprint).IsEqualTo(contract.Fingerprint);
        await Assert.That(surface.Version).IsEqualTo("v1");
        await Assert.That(surface.Pin is null).IsTrue();
        await Assert.That(surface.PinKind).IsEqualTo(ContractPinKind.None);
        await Assert.That(surface.PinMatched).IsTrue();
        await Assert.That(surface.LibraryId).IsEqualTo("billing");
        await Assert.That(session.Lower(domain, analysis).Count).IsGreaterThan(0);
    }

    [Test]
    public async Task Bind_Matching_RecordsSourceVersionFingerprintAndEndpoint() {
        var (domain, _, analysis) = Analyze(NoPinDsl, BillingSurface());
        var facts = BindFacts(domain, analysis);

        await Assert.That(facts.LibraryId).IsEqualTo("billing");
        await Assert.That(facts.Contract).IsEqualTo("Billing");
        await Assert.That(facts.SourceIdentifier).IsEqualTo("billing");
        await Assert.That(facts.Version).IsEqualTo("v1");
        await Assert.That(facts.Fingerprint).IsEqualTo(domain.ImportedContracts.Single().Fingerprint);
        await Assert.That(facts.Endpoint).IsEqualTo("Charge");
        await Assert.That(facts.AuthoredEndpoint).IsEqualTo("Charge");
        await Assert.That(facts.Action).IsEqualTo("Pay");
        await Assert.That(facts.Parameter).IsEqualTo("request");
        await Assert.That(facts.Status).IsEqualTo(ContractBindingStatus.Bound);
    }

    [Test]
    public async Task Uses_VersionPinMatchingThePackage_IsClean() {
        const string poly = """
            domain Shop
            uses billing v1

            Order: entity { Pay: action (request: ChargeRequest) { } }
            ChargeOrder: bind Billing Charge to Pay request
            """;
        var (domain, _, analysis) = Analyze(poly, BillingSurface());

        await Assert.That(analysis.HasErrors).IsFalse();
        var surface = analysis.GetMetadata<ContractSurfaceMetadata>(domain.ImportedContracts.Single())!;
        await Assert.That(surface.Pin).IsEqualTo("v1");
        await Assert.That(surface.PinKind).IsEqualTo(ContractPinKind.Version);
        await Assert.That(surface.PinMatched).IsTrue();
    }

    [Test]
    public async Task Uses_WrongVersionPin_FailsWithDmcon010AndNamesThePin() {
        const string poly = """
            domain Shop
            uses billing 2024-11-20

            Order: entity { Pay: action (request: ChargeRequest) { } }
            ChargeOrder: bind Billing Charge to Pay request
            """;
        var (domain, _, analysis) = Analyze(poly, BillingSurface());

        var finding = WithCode(analysis, "DMCON010").Single();
        await Assert.That(finding.Message).Contains("2024-11-20");
        await Assert.That(finding.Message).Contains("v1");
        await Assert.That(analysis.HasErrors).IsTrue();
        var surface = analysis.GetMetadata<ContractSurfaceMetadata>(domain.ImportedContracts.Single())!;
        await Assert.That(surface.PinMatched).IsFalse();
    }

    [Test]
    public async Task Uses_WrongPin_RecordsPinMismatchOnTheBind() {
        const string poly = """
            domain Shop
            uses billing 2024-11-20

            Order: entity { Pay: action (request: ChargeRequest) { } }
            ChargeOrder: bind Billing Charge to Pay request
            """;
        var (domain, _, analysis) = Analyze(poly, BillingSurface());

        var facts = BindFacts(domain, analysis);
        await Assert.That(facts.Endpoint).IsEqualTo("Charge");
        await Assert.That(facts.Status).IsEqualTo(ContractBindingStatus.PinMismatch);
    }

    [Test]
    public async Task Uses_FingerprintPin_MatchesTheCurrentExport() {
        var surface = BillingSurface();
        var poly = $$"""
            domain Shop
            uses billing {{surface.Fingerprint}}

            Order: entity { Pay: action (request: ChargeRequest) { } }
            ChargeOrder: bind Billing Charge to Pay request
            """;
        var (domain, session, analysis) = Analyze(poly, surface);

        await Assert.That(analysis.HasErrors).IsFalse();
        var contract = domain.ImportedContracts.Single();
        var facts = analysis.GetMetadata<ContractSurfaceMetadata>(contract)!;
        await Assert.That(facts.PinKind).IsEqualTo(ContractPinKind.Fingerprint);
        await Assert.That(facts.PinMatched).IsTrue();

        // The pin is the surface the caller named; the package version is its own fact.
        // A pin copied into Version (or the reverse) fails these.
        await Assert.That(facts.Pin).IsEqualTo(surface.Fingerprint);
        await Assert.That(facts.Version).IsEqualTo("v1");
        await Assert.That(facts.Pin).IsNotEqualTo(facts.Version);
        await Assert.That(contract.Pin).IsEqualTo(surface.Fingerprint);
        await Assert.That(contract.Version).IsEqualTo("v1");

        var bind = BindFacts(domain, analysis);
        await Assert.That(bind.Version).IsEqualTo("v1");
        await Assert.That(bind.Fingerprint).IsEqualTo(surface.Fingerprint);

        var printed = new DomainDslPrinter(session).Print(domain);
        await Assert.That(printed.Contains($"uses billing {surface.Fingerprint}", StringComparison.Ordinal)).IsTrue();
        await Assert.That(printed.Contains("Billing: contract", StringComparison.Ordinal)).IsFalse();
        await Assert.That(printed.Contains("uses billing v1", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Uses_StaleFingerprintPin_FailsWithDmcon010AndNamesThePin() {
        var stale = new string('0', 64);
        var poly = $$"""
            domain Shop
            uses billing {{stale}}

            Order: entity { Pay: action (request: ChargeRequest) { } }
            ChargeOrder: bind Billing Charge to Pay request
            """;
        var (domain, _, analysis) = Analyze(poly, BillingSurface());

        var finding = WithCode(analysis, "DMCON010").Single();
        await Assert.That(finding.Message).Contains(stale);
        var surface = analysis.GetMetadata<ContractSurfaceMetadata>(domain.ImportedContracts.Single())!;
        await Assert.That(surface.PinMatched).IsFalse();
        await Assert.That(surface.PinKind).IsEqualTo(ContractPinKind.Fingerprint);
    }

    [Test]
    public async Task FingerprintPin_ReadsAsOnePinTokenRun() {
        var fingerprint = BillingSurface().Fingerprint;
        var tree = DslSyntaxTree.Parse($"domain Shop\nuses billing {fingerprint}\n");

        await Assert.That(tree.UsesVersions.Single()).IsEqualTo(fingerprint);
    }

    [Test]
    public async Task Bind_MissingEndpoint_FailsWithDmcon013AndNamesThePin() {
        const string poly = """
            domain Shop
            uses billing v1

            Order: entity { Pay: action (request: ChargeRequest) { } }
            ChargeOrder: bind Billing Nope to Pay request
            """;
        var (domain, _, analysis) = Analyze(poly, BillingSurface());

        var finding = WithCode(analysis, "DMCON013").Single();
        await Assert.That(finding.Message).Contains("Nope");
        await Assert.That(finding.Message).Contains("v1");
        await Assert.That(BindFacts(domain, analysis).Status).IsEqualTo(ContractBindingStatus.NotOnSurface);
    }

    [Test]
    public async Task Bind_ContractNotRegistered_RecordsUnresolved() {
        const string poly = """
            domain Shop

            Order: entity { Pay: action (request: Text) { } }
            ChargeOrder: bind Missing Charge to Pay request
            """;
        var (domain, _, analysis) = Analyze(poly, BillingSurface());

        await Assert.That(WithCode(analysis, DomainModelDiagnosticCodes.ContractReferenceUnresolved).Single().Message).Contains("not registered");
        var facts = BindFacts(domain, analysis);
        await Assert.That(facts.Status).IsEqualTo(ContractBindingStatus.Unresolved);
        await Assert.That(facts.Contract).IsEqualTo("Missing");
    }

    [Test]
    public async Task Bind_OriginalName_ExactPayloadResolvesTheSuffixedEndpointSilently() {
        var surface = OverloadSurface([
            ("Charge__Money", "Charge", "Money"),
            ("Charge__Text", "Charge", "Text"),
        ]);
        const string poly = """
            domain Shop
            uses billing

            Order: entity { Pay: action (request: Money) { } }
            ChargeOrder: bind Billing Charge to Pay request
            """;
        var (domain, _, analysis) = Analyze(poly, surface);

        await Assert.That(analysis.HasErrors).IsFalse();
        await Assert.That(WithCode(analysis, "DMCON011")).IsEmpty();
        var facts = BindFacts(domain, analysis);
        await Assert.That(facts.Endpoint).IsEqualTo("Charge__Money");
        await Assert.That(facts.AuthoredEndpoint).IsEqualTo("Charge");
        await Assert.That(facts.Status).IsEqualTo(ContractBindingStatus.Bound);
    }

    [Test]
    public async Task Bind_OriginalName_MoreConstrainedPayloadWinsAndLowerSplicesIt() {
        var surface = OverloadSurface([
            ("Charge__Amount", "Charge", "Amount"),
            ("Charge__Small", "Charge", "Small"),
        ]);
        var bodies = new Dictionary<string, Func<Poly.Ast.Nodes.Block>>(StringComparer.Ordinal) {
            ["Charge__Amount"] = TraceBody("Billing.Charge__Amount"),
            ["Charge__Small"] = TraceBody("Billing.Charge__Small"),
        };
        const string poly = """
            domain Shop
            uses billing

            Order: entity { Pay: action (request: Money) { } }
            ChargeOrder: bind Billing Charge to Pay request
            """;
        var (domain, session, analysis) = Analyze(poly, surface, bodies: bodies);

        await Assert.That(analysis.HasErrors).IsFalse();
        var warning = WithCode(analysis, "DMCON011").Single();
        await Assert.That(warning.Message).Contains("Charge__Small");
        var facts = BindFacts(domain, analysis);
        await Assert.That(facts.Endpoint).IsEqualTo("Charge__Small");
        await Assert.That(facts.Status).IsEqualTo(ContractBindingStatus.Bound);

        var files = session.Emit(domain, analysis);
        var order = files.Single(file => file.FileName == "Order.cs").Source;
        await Assert.That(order).Contains("Contract endpoint 'Billing.Charge__Small' request:");
        await Assert.That(order.Contains("Contract endpoint 'Billing.Charge__Amount' request:", StringComparison.Ordinal)).IsFalse();

        var exec = ModuleExec.For(domain);
        var instance = exec.Create("Order", new Dictionary<string, object?>()).Instance!;
        var request = new Dictionary<string, object?> { ["Value"] = 10L };
        await Assert.That(exec.Invoke(instance, "Pay", new Dictionary<string, object?> { ["request"] = request }).Succeeded).IsTrue();
    }

    [Test]
    public async Task Bind_OriginalName_IncomparableAcceptingPayloads_IsDmcon012() {
        var surface = OverloadSurface([
            ("Charge__Small", "Charge", "Small"),
            ("Charge__Wide", "Charge", "Wide"),
        ]);
        const string poly = """
            domain Shop
            uses billing

            Order: entity { Pay: action (request: Money) { } }
            ChargeOrder: bind Billing Charge to Pay request
            """;
        var (domain, _, analysis) = Analyze(poly, surface);

        var finding = WithCode(analysis, "DMCON012").Single();
        await Assert.That(finding.Message).Contains("Charge__Small");
        await Assert.That(finding.Message).Contains("Charge__Wide");
        await Assert.That(BindFacts(domain, analysis).Status).IsEqualTo(ContractBindingStatus.Ambiguous);
    }

    [Test]
    public async Task Bind_ParameterTypeThePayloadCannotAccept_IsIncompatible() {
        var surface = OverloadSurface([
            ("Charge__Text", "Charge", "TextPayload"),
        ]);
        const string poly = """
            domain Shop
            uses billing

            Order: entity { Pay: action (request: Money) { } }
            ChargeOrder: bind Billing Charge to Pay request
            """;
        var (domain, _, analysis) = Analyze(poly, surface);

        var finding = analysis.Diagnostics.Single(d => d.Message.Contains("incompatible", StringComparison.Ordinal));
        await Assert.That(finding.Message).Contains("Money");
        await Assert.That(BindFacts(domain, analysis).Status).IsEqualTo(ContractBindingStatus.Incompatible);
    }

    [Test]
    public async Task StageDispatchedBoundAction_DifferingParameterTypes_DoesNotBindOrSplice() {
        const string poly = """
            domain Shop
            uses billing

            Order: entity {
              Open: stage {
                Pay: action (request: ChargeRequest) { transition to Done }
              }
              Done: stage {
                Pay: action (request: Text) { }
              }
            }

            ChargeOrder: bind Billing Charge to Pay request
            """;
        var bodies = new Dictionary<string, Func<Poly.Ast.Nodes.Block>>(StringComparer.Ordinal) {
            ["Charge"] = TraceBody("Billing.Charge"),
        };
        var (domain, session, analysis) = Analyze(
            poly, BillingSurface(ContractSourceKind.ExternalProvider), bodies: bodies);
        var facts = analysis.GetMetadata<ContractBindingMetadata>(domain.ContractBindings.Single());

        await Assert.That(analysis.HasErrors).IsTrue();
        await Assert.That(facts!.Status).IsNotEqualTo(ContractBindingStatus.Bound);
        await Assert.That(() => session.Emit(domain, analysis)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task StageDispatchedBoundAction_AllocatesDistinctSpliceLocals() {
        const string poly = """
            domain Shop
            uses stripe

            Order: entity {
              Open: stage {
                Pay: action (request: ChargeRequest) { transition to Done }
              }
              Done: stage {
                Pay: action (request: ChargeRequest) { }
              }
            }

            ChargeOrder: bind Stripe Charge to Pay request
            """;
        var (domain, session, analysis) = Analyze(poly, StripeLibrary());

        var files = session.Emit(domain, analysis);
        var order = files.Single(file => file.FileName == "Order.cs").Source;
        await Assert.That(order).Contains("adapterResult0");
        await Assert.That(order).Contains("adapterResult1");

        var exec = ModuleExec.For(domain);
        var instance = exec.Create("Order", new Dictionary<string, object?>()).Instance!;
        var request = new Dictionary<string, object?> { ["Amount"] = 10L };
        await Assert.That(exec.Invoke(instance, "Pay", new Dictionary<string, object?> { ["request"] = request }).Succeeded).IsTrue();
    }
}