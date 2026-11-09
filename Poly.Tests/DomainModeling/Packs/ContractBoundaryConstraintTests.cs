using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.ContractFill;
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

    private static ContractBindingStatus BindStatus(Domain domain, AnalysisResult analysis) =>
        (analysis.GetMetadata<ContractBindingMetadata>(domain.ContractBindings.Single())
        ?? throw new InvalidOperationException("No binding facts were recorded.")).Status;

    private static ImportedContract SurfaceWith(params Property[] properties) =>
        new(
            "Billing",
            ContractSourceKind.ExternalProvider,
            "billing",
            "v1",
            [
                new ContractEndpoint(
                    "Charge",
                    ContractEndpointKind.Operation,
                    ContractEndpointDirection.Outbound,
                    new DomainTypeReference("ChargeRequest")) {
                    SourceName = "Charge",
                },
            ]) {
            Types = [new ValueType("ChargeRequest", properties, [])],
        };

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
        // S1: a proven bind is silent — no unprovable obligation, so no DMCON015 either.
        await Assert.That(WithCode(analysis, "DMCON015")).IsEmpty();
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
    /// equality cannot be shown to sit inside a range, so <c>MergeResult</c> returns
    /// <c>Unmergeable</c> from its different-types guard (<c>ConstraintMerge.cs:54</c>);
    /// <c>ConstraintObligation</c> treats that exactly like "no constraint of this kind" and
    /// reports unknown. Never <c>Empty</c>, never a violation.
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

    /// <summary>
    /// Bug 1 (blocking): an exported <c>require</c> is written against the <b>entity</b>, so a
    /// bare <see cref="PropertyAccess"/> in it is entity state, not the argument. Reading it on
    /// the caller's payload turned a legal call into <c>DMCON014</c>. A name collision between
    /// the entity and the payload is undecidable at the bind seam, so it is unknown.
    /// </summary>
    [Test]
    public async Task EntityScopedPrecondition_IsUnknownNeverProvenOrViolated() {
        var satisfying = Analyze(CallerDsl("range(1, 10)"), Surface(preconditions: [AmountPositive()]));
        await Assert.That(WithCode(satisfying.Analysis, "DMCON014")).IsEmpty();
        await Assert.That(Boundary(satisfying.Domain, satisfying.Analysis).Precondition)
            .IsEqualTo(ContractBoundaryVerdict.Unknown);

        var violating = Analyze(CallerDsl("range(-5, -1)"), Surface(preconditions: [AmountPositive()]));
        await Assert.That(WithCode(violating.Analysis, "DMCON014")).IsEmpty();
        await Assert.That(Boundary(violating.Domain, violating.Analysis).Precondition)
            .IsEqualTo(ContractBoundaryVerdict.Unknown);
        await Assert.That(violating.Analysis.HasErrors).IsFalse();
    }

    /// <summary>
    /// The same rule on real <see cref="InternalDomainProducer"/> output — the shape that
    /// caught the bug. The producer exports <c>require Positive</c> where
    /// <c>Positive: policy { Amount &gt; 0 }</c> is about <c>Ledger</c>, while the payload is
    /// <c>ChargeRequest</c> and the caller passes <c>range(-5, -1)</c>. <c>origin/master</c>
    /// reports nothing here; the check must not either.
    /// </summary>
    [Test]
    public async Task EntityScopedPrecondition_RealProducedContract_IsNeverViolated() {
        var (domain, session, analysis) = AnalyzeProducedBind(ProducerDslWithOneRequire);

        await Assert.That(WithCode(analysis, "DMCON014")).IsEmpty();
        await Assert.That(analysis.HasErrors).IsFalse();
        await Assert.That(Boundary(domain, analysis).Precondition)
            .IsEqualTo(ContractBoundaryVerdict.Unknown);
        await Assert.That(session.Lower(domain, analysis).Count).IsGreaterThan(0);
    }

    /// <summary>
    /// S1: an entity-scoped <c>require</c> cannot be cleared by the caller, so every one of
    /// them is its own <c>DMCON015</c>, and an endpoint carrying none is silent. This is the
    /// noise real models reach — the DSL cannot author a parameter-scoped precondition at all —
    /// so it is pinned here rather than left to the reader.
    /// </summary>
    [Test]
    public async Task EntityScopedPreconditions_AreReportedOncePerRequire() {
        var (_, _, twoRequires) = AnalyzeProducedBind(ProducerDslWithTwoRequires);
        await Assert.That(twoRequires.HasErrors).IsFalse();
        await Assert.That(WithCode(twoRequires, "DMCON014")).IsEmpty();
        await Assert.That(WithCode(twoRequires, "DMCON015").Count).IsEqualTo(2);

        var (_, _, noRequires) = AnalyzeProducedBind(ProducerDslWithoutRequires);
        await Assert.That(WithCode(noRequires, "DMCON015")).IsEmpty();
        await Assert.That(noRequires.HasErrors).IsFalse();
    }

    private const string ProducerDslWithOneRequire = """
        domain billing

        public value ChargeRequest {
          Amount: Number
        }

        public entity Ledger {
          Amount: Number
          Positive: policy { Amount > 0 }
          public action Charge (line: ChargeRequest)
            require Positive
          {
          }
        }
        """;

    private const string ProducerDslWithTwoRequires = """
        domain billing

        public value ChargeRequest {
          Amount: Number
        }

        public entity Ledger {
          Amount: Number
          Positive: policy { Amount > 0 }
          UnderHundred: policy { Amount < 100 }
          public action Charge (line: ChargeRequest)
            require Positive, UnderHundred
          {
          }
        }
        """;

    private const string ProducerDslWithoutRequires = """
        domain billing

        public value ChargeRequest {
          Amount: Number
        }

        public entity Ledger {
          Amount: Number
          public action Charge (line: ChargeRequest) {
          }
        }
        """;

    /// <summary>
    /// Produces the contract from a real source document and binds the standard caller to it,
    /// so a test exercises <see cref="InternalDomainProducer"/> output rather than a hand-built
    /// surface. The caller's payload does not share a property obligation with the export, so
    /// the only diagnostics available are the precondition ones.
    /// </summary>
    private static (Domain Domain, DomainSession Session, AnalysisResult Analysis) AnalyzeProducedBind(
        string sourceDsl) {
        var parsed = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(sourceDsl).Parse());
        if (!parsed.Succeeded)
            throw new InvalidOperationException(parsed.FailureSummary ?? "source analysis failed");

        return Analyze("""
            domain Shop
            uses billing

            SmallRequest: value {
              Amount: Number range(-5, -1)
            }

            Order: entity {
              Pay: action (request: SmallRequest) { }
            }

            ChargeOrder: bind Billing Charge to Pay request
            """, new InternalDomainProducer().Produce(parsed.Root!));
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

    /// <summary>
    /// Bug 2: <see cref="ConstraintMerge.MergeResult"/> returns <c>Empty</c> for two different
    /// <c>pattern</c> strings, which is not a disjointness proof (both can accept the same
    /// input). <c>origin/master</c> refused this input as <c>DMCON001</c>, so it stays on the
    /// incompatibility path instead of gaining a false "disjoint" verdict.
    /// </summary>
    [Test]
    public async Task PatternMismatch_StaysOnTheIncompatibilityPath() {
        var surface = SurfaceWith(
            new Property("Code", new DomainTypeReference("Text"), [new PatternConstraint("^[0-9]+$")]));
        var (domain, _, analysis) = Analyze("""
            domain Shop
            uses billing

            SmallCode: value {
              Code: Text pattern("^[0-9]{1,6}$")
            }

            Order: entity {
              Pay: action (request: SmallCode) { }
            }

            ChargeOrder: bind Billing Charge to Pay request
            """, surface);

        await Assert.That(WithCode(analysis, "DMCON014")).IsEmpty();
        await Assert.That(WithCode(analysis, "DMCON001")).IsNotEmpty();
        await Assert.That(BindStatus(domain, analysis)).IsEqualTo(ContractBindingStatus.Incompatible);
        await Assert.That(analysis.GetMetadata<ContractBoundaryMetadata>(
            domain.ContractBindings.Single()) is null).IsTrue();
    }

    /// <summary>
    /// Bug 2, the <c>default</c> arm: two different defaults are not disjoint values either —
    /// a default is not a restriction at all. Same outcome as the pattern arm: <c>DMCON001</c>,
    /// as on <c>origin/master</c>.
    /// </summary>
    [Test]
    public async Task DefaultMismatch_StaysOnTheIncompatibilityPath() {
        var surface = SurfaceWith(
            new Property("Amount", new DomainTypeReference("Number"),
                [new DefaultValueConstraint(DomainExpression.Literal(5))]));
        var (domain, _, analysis) = Analyze("""
            domain Shop
            uses billing

            SmallRequest: value {
              Amount: Number default(6)
            }

            Order: entity {
              Pay: action (request: SmallRequest) { }
            }

            ChargeOrder: bind Billing Charge to Pay request
            """, surface);

        await Assert.That(WithCode(analysis, "DMCON014")).IsEmpty();
        await Assert.That(WithCode(analysis, "DMCON001")).IsNotEmpty();
        await Assert.That(BindStatus(domain, analysis)).IsEqualTo(ContractBindingStatus.Incompatible);
        await Assert.That(analysis.GetMetadata<ContractBoundaryMetadata>(
            domain.ContractBindings.Single()) is null).IsTrue();
    }

    /// <summary>
    /// A shape mismatch is not a constraint question: <see cref="ContractPayloadRelation.SameShape"/>
    /// is false, so no boundary verdict exists and the bind stays <c>Incompatible</c>.
    /// </summary>
    [Test]
    public async Task ShapeMismatch_StaysIncompatible() {
        var surface = SurfaceWith(
            new Property("Amount", new DomainTypeReference("Number"), []),
            new Property("Currency", new DomainTypeReference("Text"), []));
        var (domain, _, analysis) = Analyze("""
            domain Shop
            uses billing

            SmallRequest: value {
              Amount: Number range(0, 10)
            }

            Order: entity {
              Pay: action (request: SmallRequest) { }
            }

            ChargeOrder: bind Billing Charge to Pay request
            """, surface);

        await Assert.That(WithCode(analysis, "DMCON014")).IsEmpty();
        await Assert.That(WithCode(analysis, "DMCON001")).IsNotEmpty();
        await Assert.That(BindStatus(domain, analysis)).IsEqualTo(ContractBindingStatus.Incompatible);
        await Assert.That(analysis.GetMetadata<ContractBoundaryMetadata>(
            domain.ContractBindings.Single()) is null).IsTrue();
    }

    /// <summary>
    /// A precondition is decided only when it names the bound argument. A strict bound is never
    /// widened into a proof, a non-numeric literal is never a proof, and only a caller bound the
    /// merge can decide is proven or violated.
    /// </summary>
    [Test]
    public async Task ParameterScopedPrecondition_StrictBoundsAreNeverWidenedIntoAProof() {
        static DomainExpression Request() => DomainExpression.Parameter("request");

        static DomainExpression GreaterThan(object value) =>
            DomainExpression.GreaterThan(Request(), DomainExpression.Literal(value));

        var ten = new RangeConstraint(0, 10);

        await Assert.That(ParameterVerdict(GreaterThan(0), [ten]))
            .IsEqualTo(ContractBoundaryVerdict.Unknown);
        await Assert.That(ParameterVerdict(
            DomainExpression.LessThan(Request(), DomainExpression.Literal(10)), [ten]))
            .IsEqualTo(ContractBoundaryVerdict.Unknown);
        await Assert.That(ParameterVerdict(
            DomainExpression.GreaterThanOrEqual(Request(), DomainExpression.Literal(10)), [ten]))
            .IsEqualTo(ContractBoundaryVerdict.Unknown);
        await Assert.That(ParameterVerdict(GreaterThan(0), []))
            .IsEqualTo(ContractBoundaryVerdict.Unknown);
        await Assert.That(ParameterVerdict(GreaterThan(0.5), [ten]))
            .IsEqualTo(ContractBoundaryVerdict.Unknown);
        await Assert.That(ParameterVerdict(
            DomainExpression.Equal(Request(), DomainExpression.Literal(5)), [ten]))
            .IsEqualTo(ContractBoundaryVerdict.Unknown);
        await Assert.That(ParameterVerdict(GreaterThan("abc"), [ten]))
            .IsEqualTo(ContractBoundaryVerdict.Unknown);

        await Assert.That(ParameterVerdict(
            DomainExpression.Equal(Request(), DomainExpression.Literal(5)),
            [new RangeConstraint(5, 5)]))
            .IsEqualTo(ContractBoundaryVerdict.Proven);

        var violated = AnalyzeParameterPrecondition(GreaterThan(0), [new RangeConstraint(-5, -1)]);
        await Assert.That(WithCode(violated.Analysis, "DMCON014")).IsNotEmpty();
        await Assert.That(() => violated.Session.Lower(violated.Domain, violated.Analysis))
            .Throws<InvalidOperationException>();

        await Assert.That(ParameterVerdict(GreaterThan(10), [ten]))
            .IsEqualTo(ContractBoundaryVerdict.Violated);
        await Assert.That(ParameterVerdict(
            DomainExpression.GreaterThanOrEqual(Request(), DomainExpression.Literal(11)), [ten]))
            .IsEqualTo(ContractBoundaryVerdict.Violated);
    }

    /// <summary>
    /// The DSL cannot author constraints on an action parameter, so this builds the caller's
    /// argument with its own constraints the way a narrowed parameter would carry them.
    /// </summary>
    private static (Domain Domain, DomainSession Session, AnalysisResult Analysis) AnalyzeParameterPrecondition(
        DomainExpression precondition,
        IReadOnlyList<Constraint> callerConstraints) {
        var surface = new ImportedContract(
            "Billing",
            ContractSourceKind.ExternalProvider,
            "billing",
            "v1",
            [
                new ContractEndpoint(
                    "Charge",
                    ContractEndpointKind.Operation,
                    ContractEndpointDirection.Outbound,
                    new DomainTypeReference("Number")) {
                    SourceName = "Charge",
                    Preconditions = [new Policy("Positive", precondition)],
                },
            ]) { Types = [] };

        var tree = DslSyntaxTree.Parse("""
            domain Shop
            uses billing

            Order: entity { Pay: action { } }
            ChargeOrder: bind Billing Charge to Pay request
            """);
        var session = DomainSession.ForDocument(tree, [], Catalog(new ContractLibrary("billing", surface)));
        var changes = new List<DomainChange>(new DomainResolver(tree, session).Resolve());
        changes.Add(new AddParameterToActionChange(
            "Order", "Pay", new Property("request", new DomainTypeReference("Number"), callerConstraints)));
        var applied = new DomainEvolution(new Domain("_", [])).Apply(changes, session: session);
        return (applied.Root, session, session.Analyze(applied.Root));
    }

    private static ContractBoundaryVerdict? ParameterVerdict(
        DomainExpression precondition,
        IReadOnlyList<Constraint> callerConstraints) {
        var (domain, _, analysis) = AnalyzeParameterPrecondition(precondition, callerConstraints);
        return analysis.GetMetadata<ContractBoundaryMetadata>(domain.ContractBindings.Single())
            ?.Precondition;
    }
}