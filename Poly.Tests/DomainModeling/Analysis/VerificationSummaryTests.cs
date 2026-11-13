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
    /// an error and (since V-05b S3b) records a violation fact for it. The callee's own parameter arm also records an
    /// unverified fact on a different node. Since S3b this is no longer a same-node masking case: the
    /// error node carries a violation fact, and the "error with no fact" arm is covered by
    /// <c>LookupNullError_IsViolated</c>.
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

        // The probe: the error comes from the call-chain arm and (V-05b S3b) now carries a violation fact.
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error
            && d.Message.StartsWith("Call-chain postcondition (A → B)", StringComparison.Ordinal))).IsTrue();
        await Assert.That(facts.Violations).IsNotEmpty();
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

    // ── S6: the warn-as-error primitive (D8) ──────────────────────────────

    private const string UnboundedWrite = """
        domain Test
        Stock: entity {
          Reserved: Number range(0, 100000)
          Release: action (qty: Number) { assign Reserved to Reserved - qty }
        }
        """;

    private static Diagnostic Rewritten(IReadOnlyList<Diagnostic> diagnostics, Diagnostic original) =>
        diagnostics.Single(d => ReferenceEquals(d.Node, original.Node) && d.Code == original.Code);

    [Test]
    public async Task WarningPromotion_PromotesTheListedCode() {
        var result = Parse(UnboundedWrite);
        var before = WithCode(result.Analysis, DomainModelDiagnosticCodes.ConstraintUndecided).Single();
        await Assert.That(before.Severity).IsEqualTo(DiagnosticSeverity.Warning);

        var promoted = WarningPromotion.Apply(
            result.Analysis.Diagnostics, new HashSet<string>(StringComparer.Ordinal) { "DMCS003" });

        var after = Rewritten(promoted, before);
        await Assert.That(after.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(after.Message).IsEqualTo(before.Message);
        await Assert.That(ReferenceEquals(after.Node, before.Node)).IsTrue();
        // The result is a copy: the analysis keeps the reported severity.
        await Assert.That(Rewritten(result.Analysis.Diagnostics, before).Severity)
            .IsEqualTo(DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task WarningPromotion_LeavesAnUnlistedCodeAlone() {
        var (_, analysis) = RunFlowed(
            FlowedDomain,
            [new ObservedFlow("Customer.Notes", "Order.Notes")],
            [new DiagnosticAllowance(
                DomainModelDiagnosticCodes.EffectConstraintViolation, "Orders/Order/Notes", ["plsql-1"])]);
        var before = WithCode(analysis, DomainModelDiagnosticCodes.EffectConstraintViolation).Single();
        await Assert.That(before.Severity).IsEqualTo(DiagnosticSeverity.Warning);

        var promoted = WarningPromotion.Apply(
            analysis.Diagnostics, new HashSet<string>(StringComparer.Ordinal) { "DMCS003" });

        await Assert.That(Rewritten(promoted, before).Severity).IsEqualTo(DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task WarningPromotion_WithoutCodes_PromotesEveryWarning() {
        var (_, analysis) = RunFlowed(
            FlowedDomain,
            [new ObservedFlow("Customer.Notes", "Order.Notes")],
            [new DiagnosticAllowance(
                DomainModelDiagnosticCodes.EffectConstraintViolation, "Orders/Order/Notes", ["plsql-1"])]);

        var promoted = WarningPromotion.Apply(analysis.Diagnostics);

        await Assert.That(promoted.Count).IsEqualTo(analysis.Diagnostics.Count);
        await Assert.That(promoted.Any(d => d.Severity == DiagnosticSeverity.Warning)).IsFalse();
        await Assert.That(promoted.Count(d => d.Severity == DiagnosticSeverity.Error))
            .IsEqualTo(analysis.Diagnostics.Count(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
    }

    [Test]
    public async Task EveryUnverifiedReason_HasAnExplicitRank() {
        // A2: each reason names its own rank, so a new one cannot silently tie with PossibleViolation,
        // and an undeclared value is a guard failure rather than a rank of zero.
        var ladder = new[] {
            UnverifiedReason.PossibleViolation,
            UnverifiedReason.UnknownBound,
            UnverifiedReason.UnsupportedOperator,
            UnverifiedReason.NonFlowCheckedKind,
            UnverifiedReason.RecursionCut,
        };
        await Assert.That(Enum.GetValues<UnverifiedReason>()).IsEquivalentTo(ladder);

        for (var i = 1; i < ladder.Length; i++) {
            var lower = WriteVerdict.Unverified(ladder[i - 1], DomainModelDiagnosticCodes.ConstraintUndecided);
            var higher = WriteVerdict.Unverified(ladder[i], DomainModelDiagnosticCodes.ConstraintUndecided);
            await Assert.That(lower.Worst(higher)).IsEqualTo(higher);
            await Assert.That(higher.Worst(lower)).IsEqualTo(higher);
        }
        await Assert.That(() => WriteVerdict.Unverified((UnverifiedReason)99, DomainModelDiagnosticCodes.ConstraintUndecided).Worst(WriteVerdict.Satisfied))
            .Throws<ArgumentOutOfRangeException>();
    }

    // ── fix round 1: row enrichment, orphan facts, verdict defaults ───────

    [Test]
    public async Task TwoFlowsOfDifferentKindsIntoOneTarget_EachRowCarriesItsOwnReason_F4() {
        // Two flows into one target property: one source carries no range (DMCS003, UnknownBound) and
        // one is wider than the target (DMEFF008, PossibleViolation, downgraded by the allowance so it
        // is a row). Each row must take the fact of ITS OWN finding, not the first fact on the node.
        var (domain, analysis) = RunFlowed("""
            domain Orders
            Wide: entity {
              Amount: Number range(0, 20)
            }
            Unbounded: entity {
              Amount: Number
            }
            Order: entity {
              Amount: Number range(0, 10)
            }
            """, [
                new ObservedFlow("Unbounded.Amount", "Order.Amount"),
                new ObservedFlow("Wide.Amount", "Order.Amount"),
            ], [new DiagnosticAllowance(
                DomainModelDiagnosticCodes.EffectConstraintViolation, "Orders/Order/Amount", ["plsql-1"])]);

        var summary = analysis.GetVerification(domain);

        await Assert.That(summary.Verdict).IsEqualTo(CheckVerdict.Unknown);
        await Assert.That(summary.UnverifiedWrites.Count).IsEqualTo(2);
        var undecided = summary.UnverifiedWrites.Single(row => row.Code == DomainModelDiagnosticCodes.ConstraintUndecided);
        var wider = summary.UnverifiedWrites.Single(row => row.Code == DomainModelDiagnosticCodes.EffectConstraintViolation);
        await Assert.That(undecided.Reason).IsEqualTo(UnverifiedReason.UnknownBound);
        await Assert.That(wider.Reason).IsEqualTo(UnverifiedReason.PossibleViolation);
        await Assert.That(wider.Property).IsEqualTo("Amount");
    }

    [Test]
    public async Task FactWithNoDiagnostic_IsUnknown_S1() {
        // S1/A2: a recorded fact with no finding still means Unknown, and still shows a row — Unknown
        // never has an empty row list when the bucket exists and the scope names an element.
        var result = Parse(UnboundedWrite);
        var stripped = result.Analysis with { Diagnostics = [] };

        var summary = stripped.GetVerification(result.Root);

        await Assert.That(summary.Verdict).IsEqualTo(CheckVerdict.Unknown);
        var row = summary.UnverifiedWrites.Single();
        await Assert.That(row.Code).IsEqualTo(DomainModelDiagnosticCodes.ConstraintUndecided);
        await Assert.That(row.Property).IsEqualTo("Reserved");
        await Assert.That(row.Constraint).IsEqualTo("range(0, 100000)");
        await Assert.That(row.Reason).IsEqualTo(UnverifiedReason.UnknownBound);
    }

    [Test]
    public async Task LengthParameterWrite_RowCarriesItsFact() {
        // A parameter with no `length` constraint flowing into a length-constrained property warns
        // (DMEFF008). Length is write-relevant since V-08, so the write records a fact and the row
        // is enriched with its property, constraint and reason (V-04 pinned this row as null).
        var text = new DmPrimitiveType("Text", PrimitiveSemantics.Text, []);
        var entity = new Entity("Item",
            [new Property("Notes", new DomainTypeReference("Text"), [new LengthConstraint(0, 50)])],
            [new DmAction("Set", InvocationResult.Void,
                [new Property("note", new DomainTypeReference("Text"), [])],
                [new AssignEffect(DomainExpression.Property("Notes"), DomainExpression.Parameter("note"))],
                [])],
            [], []);
        var model = DomainTestFactory.Create("Test", [text, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(model);

        var finding = WithCode(analysis, DomainModelDiagnosticCodes.EffectConstraintViolation).Single();
        await Assert.That(finding.Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(analysis.GetMetadata<WriteVerificationFacts>(model)!.Unverified.Count).IsEqualTo(1);
        var summary = analysis.GetVerification(model);
        await Assert.That(summary.Verdict).IsEqualTo(CheckVerdict.Unknown);
        var row = summary.UnverifiedWrites.Single();
        await Assert.That(row.Code).IsEqualTo(DomainModelDiagnosticCodes.EffectConstraintViolation);
        await Assert.That(row.Property).IsEqualTo("Notes");
        await Assert.That(row.Constraint).IsEqualTo("length(0, 50)");
        await Assert.That(row.Reason).IsEqualTo(UnverifiedReason.UnknownBound);
    }

    [Test]
    public async Task Division_IsUnknown() {
        // #579: `/` is not modelled by the interval flow; the write is unverified, never proven.
        var result = Parse("""
            domain Test
            Box: entity {
              X: Number range(0, 10)
              Y: Number range(0, 3)
              Run: action { assign Y to X / 2 }
            }
            """);

        var summary = result.Analysis.GetVerification(result.Root);

        await Assert.That(summary.Verdict).IsEqualTo(CheckVerdict.Unknown);
        await Assert.That(summary.UnverifiedWrites.Single().Reason).IsEqualTo(UnverifiedReason.UnsupportedOperator);
    }

    [Test]
    public async Task MatchResult_IsUnknown() {
        // #608: a `match` result has no interval, so the write is unverified, never proven.
        var result = Parse("""
            domain Test
            Inv: entity {
              Qty: Number range(0, 100) default(0)
              Open: stage {
                MatchOver: action { assign Qty to match { Qty >= 0 -> 500, else -> 1 } }
              }
            }
            """);

        var summary = result.Analysis.GetVerification(result.Root);

        await Assert.That(summary.Verdict).IsEqualTo(CheckVerdict.Unknown);
        await Assert.That(summary.UnverifiedWrites.Single().Reason).IsEqualTo(UnverifiedReason.UnsupportedOperator);
    }

    [Test]
    public async Task ObservedFlow_WiderRangeSource_IsPossibleViolation() {
        // A4(ii)/(iii): both sides are `range`, DecideRange is not Satisfied, so the finding stays
        // and a PossibleViolation fact sits beside it. The finding is an ERROR, so this write is
        // Violated and the fact takes no row — G3 excludes a fact whose node carries an in-scope
        // error, or else a Violated write would also be reported as an unknown one (fix round 2).
        var (domain, analysis) = RunFlowed("""
            domain Orders
            Source: entity {
              Amount: Number range(0, 20)
            }
            Target: entity {
              Amount: Number range(0, 10)
            }
            """, [new ObservedFlow("Source.Amount", "Target.Amount")]);

        await Assert.That(analysis.GetMetadata<WriteVerificationFacts>(domain)!.Unverified.Single().Reason)
            .IsEqualTo(UnverifiedReason.PossibleViolation);
        var summary = analysis.GetVerification(domain);
        await Assert.That(summary.Verdict).IsEqualTo(CheckVerdict.Violated);
        await Assert.That(summary.Violations).IsEqualTo(1);
        await Assert.That(summary.UnverifiedWrites).IsEmpty();
    }

    [Test]
    public async Task DefaultVerdict_IsUnknown() {
        // E5e: the zero value fails closed, so a defaulted or partially initialised verdict is never
        // read as Proven.
        static CheckVerdict Unset() => default;
        await Assert.That(Unset()).IsEqualTo(CheckVerdict.Unknown);
        await Assert.That(Enum.GetValues<CheckVerdict>()[0]).IsEqualTo(CheckVerdict.Unknown);
    }

    [Test]
    public async Task CallChainError_IsAttributedToTheCalleeScope() {
        // E3 (A3 lock miss): a call-chain finding sits on the callee's effect, so a caller-scoped
        // read says Proven while the whole domain says Violated. Pinned here and documented in
        // CORE §3.4, guide §8 and the GetVerification doc.
        var (model, analysis) = CallChainAgainstUnorderableTargetBound();

        await Assert.That(analysis.GetVerification(model).Verdict).IsEqualTo(CheckVerdict.Violated);
        var callee = analysis.GetVerification(model, "Item.B");
        await Assert.That(callee.Verdict).IsEqualTo(CheckVerdict.Violated);
        // G1: the callee's parameter-arm warning is one enriched row, not a warning plus an orphan.
        await Assert.That(callee.UnverifiedWrites.Count).IsEqualTo(1);
        await Assert.That(callee.UnverifiedWrites.Single().Code)
            .IsEqualTo(DomainModelDiagnosticCodes.EffectConstraintViolation);
        var caller = analysis.GetVerification(model, "Item.A");
        await Assert.That(caller.Verdict).IsEqualTo(CheckVerdict.Proven);
        await Assert.That(caller.UnverifiedWrites).IsEmpty();
    }

    [Test]
    public async Task ParameterArm_UnboundedParameter_IsOneEnrichedRow() {
        // G1: the parameter arm reports UnknownBound as a DMEFF008 warning
        // (ValidateParameterConstraintCompatibility), so the row's code is DMEFF008 — not the
        // DMCS003 a reason-derived mapping would guess. One finding, one enriched row.
        var entity = new Entity("Item",
            [NumProp("Total", new RangeConstraint(0d, 10d))],
            [Act("Set", [NumProp("x")],
                new AssignEffect(DomainExpression.Property("Total"), DomainExpression.Parameter("x")))],
            [], []);
        var model = Domain(entity);

        var analysis = DomainModelAnalyzer.Analyze(model);

        var summary = analysis.GetVerification(model);
        await Assert.That(summary.Verdict).IsEqualTo(CheckVerdict.Unknown);
        await Assert.That(summary.UnverifiedWrites.Count).IsEqualTo(1);
        var row = summary.UnverifiedWrites.Single();
        await Assert.That(row.Code).IsEqualTo(DomainModelDiagnosticCodes.EffectConstraintViolation);
        await Assert.That(row.Property).IsEqualTo("Total");
        await Assert.That(row.Constraint).IsEqualTo("range(0, 10)");
        await Assert.That(row.Reason).IsEqualTo(UnverifiedReason.UnknownBound);
    }
}