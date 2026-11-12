using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;

using DmAction = Poly.DomainModeling.Ontology.Action;
using DmPrimitiveType = Poly.DomainModeling.Ontology.PrimitiveType;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// V-04 (Q4/Q6): <see cref="VerificationSummaryExtensions.GetVerification"/> derives the check
/// verdict on demand. A clean model is <see cref="CheckVerdict.Proven"/>; a write V-03 could not
/// decide is <see cref="CheckVerdict.Unknown"/> with its row; a write proven outside, or any
/// in-scope error at all, is <see cref="CheckVerdict.Violated"/> — never "proven" while a write
/// finding exists.
/// </summary>
public class VerificationSummaryTests {
    private static readonly DmPrimitiveType Number = new("Number", PrimitiveSemantics.Number, []);
    private static readonly DateTime D1 = new(2020, 1, 1);
    private static readonly DateTime D2 = new(2021, 1, 1);

    private static EvolutionResult Parse(string dsl) =>
        new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(new PolyDslParser(dsl).Parse());

    private static Property NumProp(string name, params Constraint[] constraints) =>
        new(name, new DomainTypeReference("Number"), constraints);

    private static DmAction Act(string name, IReadOnlyList<Property> parameters, params Effect[] effects) =>
        new(name, InvocationResult.Void, parameters, effects, []);

    private static Domain Domain(params DomainType[] types) =>
        DomainTestFactory.Create("Test", [Number, .. types], []);

    // ── S1: the three verdicts ────────────────────────────────────────────

    [Test]
    public async Task CleanModel_IsProven() {
        var result = Parse("""
            domain Test
            Box: entity {
              A: Number range(0, 10)
              Run: action { assign A to 5 }
            }
            """);

        var summary = result.Analysis.GetVerification(result.Root);

        await Assert.That(summary.Verdict).IsEqualTo(CheckVerdict.Proven);
        await Assert.That(summary.VerifiedWrites).IsEqualTo(1);
        await Assert.That(summary.UnverifiedWrites).IsEmpty();
        await Assert.That(summary.Violations).IsEqualTo(0);
    }

    [Test]
    public async Task UnboundedParameterWrite_IsUnknownNotProven() {
        // #507: `Reserved - qty` collapses to Unknown, so the write is not decided.
        var result = Parse("""
            domain Test
            Stock: entity {
              Reserved: Number range(0, 100000)
              Release: action (qty: Number) { assign Reserved to Reserved - qty }
            }
            """);

        var summary = result.Analysis.GetVerification(result.Root);

        await Assert.That(summary.Verdict).IsEqualTo(CheckVerdict.Unknown);
        await Assert.That(summary.VerifiedWrites).IsEqualTo(0);
        await Assert.That(summary.Violations).IsEqualTo(0);
        var row = summary.UnverifiedWrites.Single();
        await Assert.That(row.Path).IsEqualTo("Test/Stock/Release");
        await Assert.That(row.Code).IsEqualTo(DomainModelDiagnosticCodes.ConstraintUndecided);
        await Assert.That(row.Property).IsEqualTo("Reserved");
        await Assert.That(row.Constraint).IsEqualTo("range(0, 100000)");
        await Assert.That(row.Reason).IsEqualTo(UnverifiedReason.UnknownBound);
    }

    [Test]
    public async Task BarePropertyRef_IsUnknown() {
        // #650: the value range [5, 20] overlaps the target [0, 10], so the write can fall outside.
        var result = Parse("""
            domain Test
            Box: entity {
              A: Number range(0, 10)
              B: Number range(5, 20)
              Run: action { assign A to B }
            }
            """);

        var summary = result.Analysis.GetVerification(result.Root);

        await Assert.That(summary.Verdict).IsEqualTo(CheckVerdict.Unknown);
        var row = summary.UnverifiedWrites.Single();
        await Assert.That(row.Code).IsEqualTo(DomainModelDiagnosticCodes.EffectConstraintViolation);
        await Assert.That(row.Reason).IsEqualTo(UnverifiedReason.PossibleViolation);
    }

    [Test]
    public async Task ErrorModel_IsViolated() {
        // A definite violation keeps the error, so the model is never "proven".
        var entity = new Entity("Item",
            [NumProp("A", new RangeConstraint(0d, 10d))],
            [Act("Run", [], new AssignEffect(DomainExpression.Property("A"), DomainExpression.Literal(99)))],
            [], []);

        var model = Domain(entity);

        var summary = DomainModelAnalyzer.Analyze(model).GetVerification(model);

        await Assert.That(summary.Verdict).IsEqualTo(CheckVerdict.Violated);
        await Assert.That(summary.Violations).IsEqualTo(1);
        await Assert.That(summary.UnverifiedWrites).IsEmpty();
    }

    // ── S2: the fold reads findings, not just facts (A1) ──────────────────

    /// <summary>
    /// A5's fixture (from <c>ConstraintFailClosedAuditTests</c>): the callee's assignment runs under
    /// the caller's context against a target bound that cannot be ordered, so the call chain reports
    /// an error and no violation fact exists for it. The callee's own parameter arm also records an
    /// unverified fact, so this doubles as A1's masking test: a diagnostic-to-fact match must not
    /// hide the error behind that fact.
    /// </summary>
    private static (Domain Domain, AnalysisResult Analysis) CallChainAgainstUnorderableTargetBound() {
        var b = Act("B", [NumProp("x")],
            new AssignEffect(DomainExpression.Property("Total"), DomainExpression.Parameter("x")));
        var a = Act("A", [NumProp("amount", new RangeConstraint(0d, 200d))],
            new InvokeActionEffect("B", [new PropertyBinding("x", DomainExpression.Parameter("amount"))]));
        var entity = new Entity("Item",
            [NumProp("Total", new RequiredConstraint(), new RangeConstraint(D1, D2))], [a, b], [], []);
        var model = Domain(entity);
        return (model, DomainModelAnalyzer.Analyze(model));
    }

    [Test]
    public async Task UnevidencedCallChainError_IsViolatedNotProven() {
        var (model, analysis) = CallChainAgainstUnorderableTargetBound();
        var facts = analysis.GetMetadata<WriteVerificationFacts>(model)!;

        // The probe: the error comes from the call-chain arm and carries no violation fact.
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error
            && d.Message.StartsWith("Call-chain postcondition (A → B)", StringComparison.Ordinal))).IsTrue();
        await Assert.That(facts.Violations).IsEmpty();
        // ... while the callee's parameter arm did record an unverified fact.
        await Assert.That(facts.Unverified).IsNotEmpty();

        var summary = analysis.GetVerification(model);

        await Assert.That(summary.Verdict).IsEqualTo(CheckVerdict.Violated);
        await Assert.That(summary.Violations).IsEqualTo(1);
    }

    [Test]
    public async Task LookupNullError_IsViolated() {
        // ValidateDomainEffects' early return: a domain-bound pass with no type lookup bag reports a
        // non-DMEFF008 error and returns. Any in-scope error is a violation, so this is never Proven.
        var entity = new Entity("Item",
            [NumProp("A", new RangeConstraint(0d, 10d))],
            [Act("Run", [], new AssignEffect(DomainExpression.Property("A"), DomainExpression.Literal(5)))],
            [], []);
        var model = Domain(entity);

        var analysis = new AnalyzerBuilder().AddAnalyzer(new EffectAnalyzer()).Build().Analyze(model);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error
            && d.Code == DomainModelDiagnosticCodes.SemanticReferenceResolution)).IsTrue();
        var summary = analysis.GetVerification(model);
        await Assert.That(summary.Verdict).IsEqualTo(CheckVerdict.Violated);
        await Assert.That(summary.Violations).IsEqualTo(1);
        await Assert.That(summary.UnverifiedWrites).IsEmpty();
        await Assert.That(summary.VerifiedWrites).IsEqualTo(0);
    }

    // ── S3: scope (A3) ────────────────────────────────────────────────────

    private const string TwoUnverifiedEntities = """
        domain Test
        Box: entity {
          A: Number range(0, 10)
          B: Number range(5, 20)
          Run: action { assign A to B }
        }
        Crate: entity {
          Q: Number range(0, 100)
          R: Number range(50, 500)
          Fill: action { assign Q to R }
        }
        Empty: entity {
          V: Number range(0, 10)
        }
        """;

    [Test]
    public async Task SelectedPath_ScopesTheSummary() {
        var result = Parse(TwoUnverifiedEntities);

        var whole = result.Analysis.GetVerification(result.Root);
        await Assert.That(whole.Verdict).IsEqualTo(CheckVerdict.Unknown);
        await Assert.That(whole.UnverifiedWrites.Count).IsEqualTo(2);

        // Entity, both select forms (dot and slash, with and without the domain prefix).
        foreach (var select in new[] { "Box", "Test.Box", "Test/Box" }) {
            var box = result.Analysis.GetVerification(result.Root, select);
            await Assert.That(box.Verdict).IsEqualTo(CheckVerdict.Unknown);
            await Assert.That(box.UnverifiedWrites.Single().Path).IsEqualTo("Test/Box/Run");
        }

        // Entity.Action narrows further; only Box's action is in scope.
        foreach (var select in new[] { "Box.Run", "Test.Box.Run" }) {
            var run = result.Analysis.GetVerification(result.Root, select);
            await Assert.That(run.UnverifiedWrites.Single().Path).IsEqualTo("Test/Box/Run");
        }

        // A clean scope does not inherit another entity's unknown (fail-closed case 6).
        var clean = result.Analysis.GetVerification(result.Root, "Empty");
        await Assert.That(clean.Verdict).IsEqualTo(CheckVerdict.Proven);
        await Assert.That(clean.UnverifiedWrites).IsEmpty();

        // A path naming nothing is not a proof.
        var nothing = result.Analysis.GetVerification(result.Root, "Nope");
        await Assert.That(nothing.Verdict).IsEqualTo(CheckVerdict.Unknown);
        await Assert.That(nothing.UnverifiedWrites).IsEmpty();
    }

    [Test]
    public async Task EmptyEntityScope_IsProven() {
        var result = Parse(TwoUnverifiedEntities);

        var summary = result.Analysis.GetVerification(result.Root, "Empty");

        await Assert.That(summary.Verdict).IsEqualTo(CheckVerdict.Proven);
        await Assert.That(summary.UnverifiedWrites).IsEmpty();
        await Assert.That(summary.Violations).IsEqualTo(0);
    }

    [Test]
    public async Task StageActionScope_MatchesWithoutTheStageSegment() {
        var result = Parse("""
            domain Test
            Box: entity {
              A: Number range(0, 10)
              B: Number range(5, 20)
              Open: stage {
                Run: action { assign A to B }
              }
              Closed: stage { }
            }
            """);

        var whole = result.Analysis.GetVerification(result.Root);
        await Assert.That(whole.UnverifiedWrites.Single().Path).IsEqualTo("Test/Box/Open/Run");

        // `Box.Run` names the stage action without writing the stage segment.
        var action = result.Analysis.GetVerification(result.Root, "Box.Run");
        await Assert.That(action.Verdict).IsEqualTo(CheckVerdict.Unknown);
        await Assert.That(action.UnverifiedWrites.Single().Path).IsEqualTo("Test/Box/Open/Run");

        // The stage itself is a prefix scope.
        var stage = result.Analysis.GetVerification(result.Root, "Box.Closed");
        await Assert.That(stage.Verdict).IsEqualTo(CheckVerdict.Proven);
        await Assert.That(stage.UnverifiedWrites).IsEmpty();
    }

    [Test]
    public async Task UnresolvedNode_IsInScopeForEveryScope() {
        // A node that does not resolve falls back to the domain path, so it can never be scoped out
        // and let the model read Proven (A3).
        var entity = new Entity("Item",
            [NumProp("A", new RangeConstraint(0d, 10d))],
            [Act("Run", [], new AssignEffect(DomainExpression.Property("A"), DomainExpression.Literal(5)))],
            [], []);
        var model = Domain(entity);
        var analysis = new AnalyzerBuilder().AddAnalyzer(new EffectAnalyzer()).Build().Analyze(model);

        var scoped = analysis.GetVerification(model, "Item");

        await Assert.That(scoped.Verdict).IsEqualTo(CheckVerdict.Violated);
        await Assert.That(scoped.Violations).IsEqualTo(1);
    }

    [Test]
    public async Task MissingFactsBucket_IsUnknownNotProven() {
        // D3: a pipeline without the effect pass never creates the bucket, so there is nothing to
        // prove — Unknown with zero counts, never an empty Proven.
        var entity = new Entity("Item",
            [new Property("Name", new DomainTypeReference("Text"), [])],
            [], [], []);
        var domain = DomainTestFactory.Create("Test", [entity], []);

        var analysis = new AnalyzerBuilder()
            .AddAnalyzer(new StructuralDomainAnalyzer())
            .AddAnalyzer(new DomainCatalogPass())
            .Build()
            .Analyze(domain);

        await Assert.That(analysis.GetMetadata<WriteVerificationFacts>(domain)).IsNull();
        var summary = analysis.GetVerification(domain);
        await Assert.That(summary.Verdict).IsEqualTo(CheckVerdict.Unknown);
        await Assert.That(summary.VerifiedWrites).IsEqualTo(0);
        await Assert.That(summary.UnverifiedWrites).IsEmpty();
        await Assert.That(summary.Violations).IsEqualTo(0);
    }

    // ── S5: observed flows route through the recorder (A4) ────────────────

    private const string FlowedDomain = """
        domain Orders
        Customer: entity {
          Amount: Number
          Notes: Text length(0, 200)
        }
        Order: entity {
          Amount: Number range(0, 10)
          Notes: Text length(0, 50)
        }
        """;

    private static (Domain Domain, AnalysisResult Analysis) Flowed(
        params ObservedFlow[] flows) => RunFlowed(FlowedDomain, flows);

    private static (Domain Domain, AnalysisResult Analysis) RunFlowed(
        string poly, IReadOnlyList<ObservedFlow> flows, IReadOnlyList<DiagnosticAllowance>? allowances = null) {
        var session = SessionBuilder.CreateEmpty().LoadCore().LoadTemporal()
            .WithObservedFlows(flows)
            .WithDiagnosticAllowances(allowances)
            .Build();
        var domain = new DomainEvolution(new Domain("_", []))
            .Apply(new PolyDslParser(poly, session).Parse(), session: session).Root;
        return (domain, session.Analyze(domain));
    }

    private static List<Diagnostic> WithCode(AnalysisResult analysis, string code) =>
        analysis.Diagnostics.Where(d => d.Code == code).ToList();

    [Test]
    public async Task ObservedFlow_UnboundedSource_IsUnverifiedAndWarns() {
        // A4(i): the source carries no `range`, so the copy can carry any Number — a DMCS003 warning
        // naming the flow plus an unverified fact on the target, never silence.
        var (domain, analysis) = Flowed(new ObservedFlow("Customer.Amount", "Order.Amount"));

        var warning = WithCode(analysis, DomainModelDiagnosticCodes.ConstraintUndecided).Single();
        await Assert.That(warning.Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(warning.Message).Contains("Observed flow 'Customer.Amount' -> 'Order.Amount'");
        var facts = analysis.GetMetadata<WriteVerificationFacts>(domain)!;
        await Assert.That(facts.Unverified.Single().Reason).IsEqualTo(UnverifiedReason.UnknownBound);
        await Assert.That(facts.Unverified.Single().TargetProperty).IsEqualTo("Amount");

        var summary = analysis.GetVerification(domain);
        await Assert.That(summary.Verdict).IsEqualTo(CheckVerdict.Unknown);
        await Assert.That(summary.UnverifiedWrites.Single().Reason).IsEqualTo(UnverifiedReason.UnknownBound);
    }

    [Test]
    public async Task ObservedFlow_SubsumedSource_IsVerified() {
        // A4(ii): a flow whose value range sits inside the target is verified, not unknown — the
        // soundness oracle for routing flows through the recorder.
        var (domain, analysis) = RunFlowed("""
            domain Orders
            Source: entity {
              Amount: Number range(0, 5)
            }
            Target: entity {
              Amount: Number range(0, 10)
            }
            """, [new ObservedFlow("Source.Amount", "Target.Amount")]);

        await Assert.That(analysis.HasErrors).IsFalse();
        await Assert.That(analysis.Diagnostics.Any(d => d.Message.Contains("Observed flow", StringComparison.Ordinal))).IsFalse();
        var facts = analysis.GetMetadata<WriteVerificationFacts>(domain)!;
        await Assert.That(facts.Verified).IsEqualTo(1);
        await Assert.That(facts.Unverified).IsEmpty();

        var summary = analysis.GetVerification(domain);
        await Assert.That(summary.Verdict).IsEqualTo(CheckVerdict.Proven);
        await Assert.That(summary.VerifiedWrites).IsEqualTo(1);
    }

    [Test]
    public async Task ObservedFlow_NonAllowed_ReadsViolated() {
        // A4(iii): the existing DMEFF008 stays, and a PossibleViolation fact sits beside it. With no
        // allowance the finding is an error, so the model is Violated, never Proven.
        var (domain, analysis) = Flowed(new ObservedFlow("Customer.Notes", "Order.Notes"));

        var finding = WithCode(analysis, DomainModelDiagnosticCodes.EffectConstraintViolation).Single();
        await Assert.That(finding.Severity).IsEqualTo(DiagnosticSeverity.Error);
        var facts = analysis.GetMetadata<WriteVerificationFacts>(domain)!;
        await Assert.That(facts.Unverified.Single().Reason).IsEqualTo(UnverifiedReason.PossibleViolation);

        var summary = analysis.GetVerification(domain);
        await Assert.That(summary.Verdict).IsEqualTo(CheckVerdict.Violated);
        await Assert.That(summary.Violations).IsEqualTo(1);
    }

    [Test]
    public async Task ObservedFlow_AllowanceDowngraded_ReadsUnknown() {
        // The allowance downgrades the finding to a warning; the fact remains, so the model is
        // Unknown — the finding was acknowledged, not proven.
        var (domain, analysis) = RunFlowed(
            FlowedDomain,
            [new ObservedFlow("Customer.Notes", "Order.Notes")],
            [new DiagnosticAllowance(
                DomainModelDiagnosticCodes.EffectConstraintViolation, "Orders/Order/Notes", ["plsql-1"])]);

        await Assert.That(analysis.HasErrors).IsFalse();
        var finding = WithCode(analysis, DomainModelDiagnosticCodes.EffectConstraintViolation).Single();
        await Assert.That(finding.Severity).IsEqualTo(DiagnosticSeverity.Warning);

        var summary = analysis.GetVerification(domain);
        await Assert.That(summary.Verdict).IsEqualTo(CheckVerdict.Unknown);
        await Assert.That(summary.UnverifiedWrites.Single().Reason).IsEqualTo(UnverifiedReason.PossibleViolation);
    }

    [Test]
    public async Task TwoFlowsIntoOneTarget_AreOneRow() {
        // A2's de-duplication: each flow reports its own DMEFF008 on the same target property, and
        // the summary shows one row for (node, code). The allowance keeps both findings as warnings,
        // which is the row vocabulary (an error is a violation, not a row).
        var (domain, analysis) = RunFlowed("""
            domain Orders
            First: entity {
              Notes: Text length(0, 200)
            }
            Second: entity {
              Notes: Text length(0, 300)
            }
            Order: entity {
              Notes: Text length(0, 50)
            }
            """, [
                new ObservedFlow("First.Notes", "Order.Notes"),
                new ObservedFlow("Second.Notes", "Order.Notes"),
            ], [new DiagnosticAllowance(
                DomainModelDiagnosticCodes.EffectConstraintViolation, "Orders/Order/Notes", ["plsql-1"])]);

        var findings = WithCode(analysis, DomainModelDiagnosticCodes.EffectConstraintViolation).ToList();
        await Assert.That(findings.Count).IsEqualTo(2);
        await Assert.That(findings.All(d => d.Severity == DiagnosticSeverity.Warning)).IsTrue();
        var summary = analysis.GetVerification(domain);
        await Assert.That(summary.Verdict).IsEqualTo(CheckVerdict.Unknown);
        await Assert.That(summary.UnverifiedWrites.Count).IsEqualTo(1);
        await Assert.That(summary.UnverifiedWrites.Single().Path).IsEqualTo("Orders/Order/Notes");
    }
}