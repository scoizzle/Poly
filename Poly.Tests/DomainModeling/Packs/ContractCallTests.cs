using Poly.Ast.Nodes;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Constraints;
using Poly.DomainModeling.Ontology.Contract;
using Poly.Tests.TestHelpers;

using static Poly.Tests.TestHelpers.ContractContributions;

using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.Tests.DomainModeling.Packs;

/// <summary>
/// #426 slice 1 (analysis only): an expression call that names an imported contract endpoint
/// resolves, or fails, the way a bind does, and the outcome is recorded as a
/// <see cref="ContractCallMetadata"/> on the call node. Nothing here lowers, and the call's
/// value type stays unknown.
/// </summary>
public sealed class ContractCallTests {
    private static ImportedContract Surface(
        string contractName = "Billing",
        string source = "billing",
        string payload = "ChargeRequest",
        IReadOnlyList<Constraint>? amountConstraints = null) =>
        new(
            contractName,
            ContractSourceKind.ExternalProvider,
            source,
            "v1",
            [
                new ContractEndpoint(
                    "Charge",
                    ContractEndpointKind.Operation,
                    ContractEndpointDirection.Outbound,
                    new DomainTypeReference(payload)) {
                    SourceName = "Charge",
                },
            ]) {
            Types = [
                new ValueType(payload, [
                    new Property("Amount", new DomainTypeReference("Number"), amountConstraints ?? []),
                ], []),
            ],
        };

    private static (Domain Domain, AnalysisResult Analysis) Analyze(string poly, params IDomainLibrary[] libraries) {
        var tree = DslSyntaxTree.Parse(poly);
        var session = DomainSession.ForDocument(tree, [], Catalog(libraries));
        var changes = new DomainResolver(tree, session).Resolve();
        // Apply without the analysis gate: a test that expects an error still needs the
        // proposed root (and its facts), not the rolled-back seed.
        var applied = new DomainEvolution(new Domain("_", [])).Apply(changes, session: session);
        return (applied.Root, session.Analyze(applied.Root));
    }

    private static (Domain Domain, AnalysisResult Analysis) AnalyzeBilling(
        string poly, IReadOnlyList<Constraint>? endpointAmount = null) =>
        Analyze(poly, new ContractLibrary("billing", Surface(amountConstraints: endpointAmount)));

    private static IReadOnlyList<Diagnostic> WithCode(AnalysisResult analysis, string code) =>
        analysis.Diagnostics.Where(d => string.Equals(d.Code, code, StringComparison.Ordinal)).ToList();

    private static IEnumerable<FunctionCall> Calls(Node? node) {
        if (node is null)
            yield break;
        if (node is FunctionCall call)
            yield return call;
        foreach (var child in node.Children)
            foreach (var nested in Calls(child))
                yield return nested;
    }

    private static FunctionCall EntityCall(Domain domain) =>
        Calls(domain.Types.OfType<Entity>().Single()).First();

    /// <summary>
    /// The call is an action effect's value and its argument is the action's parameter, where a
    /// bind's local parameter would be. (A policy cannot name an action parameter: DMSEM005.)
    /// </summary>
    private static string Dsl(
        string amount = "range(0, 50)",
        string uses = "uses billing",
        string call = "Charge(request)",
        string extra = "") => $$"""
        domain Shop
        {{uses}}

        SmallRequest: value {
          Amount: Number {{amount}}
        }

        Order: entity {
          Total: Number optional
          Paid: Boolean optional
          Pay: action (request: SmallRequest) { assign Paid to {{call}} }
        }
        {{extra}}
        """;

    // ── (a) ─────────────────────────────────────────────────────

    [Test]
    public async Task Policy_Call_ResolvesToImportedEndpoint_RecordsSurfaceFact() {
        var (domain, analysis) = AnalyzeBilling(Dsl(), [new RangeConstraint(0, 100)]);

        await Assert.That(analysis.Diagnostics.Where(d => d.Code is "DMCON001" or "DMFUN001")).IsEmpty();
        await Assert.That(analysis.HasErrors).IsFalse();
        // DMAS001 (no stages) is the fixture's own advice; the call itself raises nothing.
        await Assert.That(analysis.Diagnostics.Where(d => d.Code != "DMAS001")).IsEmpty();
        var fact = analysis.GetMetadata<ContractCallMetadata>(EntityCall(domain))
            ?? throw new InvalidOperationException("No call fact was recorded.");
        await Assert.That(fact.Status).IsEqualTo(ContractBindingStatus.Bound);
        await Assert.That(fact.Endpoint).IsEqualTo("Charge");
        await Assert.That(fact.AuthoredEndpoint).IsEqualTo("Charge");
        await Assert.That(fact.Contract).IsEqualTo("Billing");
        await Assert.That(fact.LibraryId).IsEqualTo("billing");
        await Assert.That(fact.SourceIdentifier).IsEqualTo("billing");
        await Assert.That(fact.Version).IsEqualTo("v1");
        await Assert.That(fact.Fingerprint).IsEqualTo(domain.ImportedContracts.Single().Fingerprint);
        // The fact is the surface and nothing about where the call is written.
        await Assert.That(typeof(ContractCallMetadata).GetProperties()
            .Any(p => p.Name.Contains("Caller", StringComparison.OrdinalIgnoreCase))).IsFalse();
    }

    // ── (b) is unreachable by a name-only call: see PROOF. ──────

    // ── (c) ─────────────────────────────────────────────────────

    [Test]
    public async Task Policy_Call_PinMismatch_ReportsDmcon010() {
        var (domain, analysis) = AnalyzeBilling(
            Dsl(uses: "uses billing 2024-11-20"), [new RangeConstraint(0, 100)]);

        await Assert.That(WithCode(analysis, "DMCON010")).IsNotEmpty();
        var fact = analysis.GetMetadata<ContractCallMetadata>(EntityCall(domain))!;
        await Assert.That(fact.Status).IsEqualTo(ContractBindingStatus.PinMismatch);
        await Assert.That(fact.Endpoint).IsEqualTo("Charge");
    }

    // ── (d) ─────────────────────────────────────────────────────

    [Test]
    public async Task Policy_Call_WiderPayload_ReportsTheBindBoundaryCode() {
        // The bind path reports DMCON014 for a caller disjoint from the payload constraint
        // (ContractBoundaryConstraintTests.CallerDisjointFromPayload_IsViolatedAndRefusesLower).
        var (domain, analysis) = AnalyzeBilling(
            Dsl(amount: "range(20, 30)"), [new RangeConstraint(0, 10)]);

        var finding = WithCode(analysis, "DMCON014").Single();
        await Assert.That(finding.Message).Contains("range");
        await Assert.That(finding.Message).Contains("Contract call 'Charge'");
        await Assert.That(analysis.GetMetadata<ContractBoundaryMetadata>(EntityCall(domain))!.Precondition)
            .IsEqualTo(ContractBoundaryVerdict.Violated);
    }

    [Test]
    public async Task Policy_Call_CallerWiderThanPayload_ReportsDmcon015() {
        var (_, analysis) = AnalyzeBilling(
            Dsl(amount: "range(0, 500)"), [new RangeConstraint(0, 100)]);

        await Assert.That(WithCode(analysis, "DMCON015")).IsNotEmpty();
        await Assert.That(WithCode(analysis, "DMCON014")).IsEmpty();
    }

    // ── (e) ─────────────────────────────────────────────────────

    [Test]
    public async Task Policy_Call_NameOnTwoContracts_ReportsAmbiguityNamingBoth() {
        var (domain, analysis) = Analyze(
            Dsl(uses: "uses billing\nuses ledger"),
            new ContractLibrary("billing", Surface("Billing", "billing", "ChargeRequest")),
            new ContractLibrary("ledger", Surface("Ledger", "ledger", "LedgerRequest")));

        var finding = WithCode(analysis, "DMCON012").Single();
        await Assert.That(finding.Message).Contains("'Billing'");
        await Assert.That(finding.Message).Contains("'Ledger'");
        await Assert.That(analysis.GetMetadata<ContractCallMetadata>(EntityCall(domain))!.Status)
            .IsEqualTo(ContractBindingStatus.Ambiguous);
    }

    // ── (f) ─────────────────────────────────────────────────────

    [Test]
    public async Task FunctionBody_CallToEndpoint_ReportsDmfun005() {
        var (_, analysis) = AnalyzeBilling(
            Dsl(extra: "function Notify(r: SmallRequest): Boolean = Charge(r)"),
            [new RangeConstraint(0, 100)]);

        var finding = WithCode(analysis, "DMFUN005").Single();
        await Assert.That(finding.Message).Contains("Charge");
        await Assert.That(WithCode(analysis, "DMFUN001")).IsEmpty();
        await Assert.That(WithCode(analysis, "DMFUN004")).IsEmpty();
    }

    // ── (g) ─────────────────────────────────────────────────────

    [Test]
    public async Task Call_WithLiteralArgument_ReportsDmcon001() {
        var (domain, analysis) = AnalyzeBilling(Dsl(call: "Charge(5)"));

        await Assert.That(WithCode(analysis, "DMCON001")
            .Count(d => d.Message.Contains("Contract call 'Charge'"))).IsEqualTo(1);
        await Assert.That(analysis.GetMetadata<ContractCallMetadata>(EntityCall(domain))!.Status)
            .IsEqualTo(ContractBindingStatus.Unresolved);
    }

    [Test]
    public async Task Call_WithArithmeticArgument_ReportsDmcon001() {
        var (_, analysis) = AnalyzeBilling(Dsl(call: "Charge(Total + 1)"));

        await Assert.That(WithCode(analysis, "DMCON001")
            .Count(d => d.Message.Contains("Contract call 'Charge'"))).IsEqualTo(1);
    }

    [Test]
    public async Task Call_WithZeroArguments_ReportsDmcon001() {
        var (_, analysis) = AnalyzeBilling(Dsl(call: "Charge()"));

        var finding = WithCode(analysis, "DMCON001")
            .Single(d => d.Message.Contains("Contract call 'Charge'"));
        await Assert.That(finding.Message).Contains("exactly one argument");
    }

    // ── (h) ─────────────────────────────────────────────────────

    [Test]
    public async Task Call_InSharedRequirePolicy_ReportsOnce() {
        // Two actions require the same policy, so analysis walks its call under the entity and
        // under both actions; the finding is the call's, reported once.
        var (domain, analysis) = AnalyzeBilling("""
            domain Shop
            uses billing

            Order: entity {
              Authorised: policy { Charge(5) }
              Pay: action require Authorised { }
              Retry: action require Authorised { }
            }
            """);

        await Assert.That(WithCode(analysis, "DMCON001")
            .Count(d => d.Message.Contains("Contract call 'Charge'"))).IsEqualTo(1);
        await Assert.That(analysis.GetMetadata<ContractCallMetadata>(EntityCall(domain))!.Status)
            .IsEqualTo(ContractBindingStatus.Unresolved);
    }

    // ── (i) ─────────────────────────────────────────────────────

    [Test]
    public async Task Call_ToNameNeitherFunctionNorEndpoint_StillReportsDmfun001() {
        var (domain, analysis) = AnalyzeBilling(Dsl(call: "Nope(request)"));

        await Assert.That(WithCode(analysis, "DMFUN001")).IsNotEmpty();
        await Assert.That(analysis.GetMetadata<ContractCallMetadata>(EntityCall(domain))).IsNull();
    }

    [Test]
    public async Task Call_ToDeclaredFunctionSharingAnEndpointName_FunctionWins() {
        var (domain, analysis) = AnalyzeBilling(Dsl(
            call: "Charge(Total)",
            extra: "function Charge(a: Number): Number = a"));

        await Assert.That(WithCode(analysis, "DMFUN001")).IsEmpty();
        await Assert.That(WithCode(analysis, "DMCON001")
            .Where(d => d.Message.Contains("Contract call"))).IsEmpty();
        await Assert.That(analysis.GetMetadata<ContractCallMetadata>(EntityCall(domain))).IsNull();
    }
}