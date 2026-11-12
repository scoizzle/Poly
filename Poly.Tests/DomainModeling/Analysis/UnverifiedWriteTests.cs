using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.Introspection;

using DmAction = Poly.DomainModeling.Ontology.Action;
using DmPrimitiveType = Poly.DomainModeling.Ontology.PrimitiveType;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// V-03 / Q4 (unit B): the unverified-write channel. Every assign to a Range-constrained property
/// gets exactly ONE verdict per effect: Satisfied is silent and counted, Violated is DMEFF008
/// (error), Undecided with a known value range is DMEFF008 (warning, "can fall outside"), and
/// Undecided with no range is DMCS003 naming the write, the property and the constraint.
/// </summary>
public class UnverifiedWriteTests {
    private static readonly DmPrimitiveType Number = new("Number", PrimitiveSemantics.Number, []);

    private static EvolutionResult Parse(string dsl) {
        var changes = new PolyDslParser(dsl).Parse();
        return new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
    }

    /// <summary>The write findings (DMEFF008 or DMCS003) that name <paramref name="property"/>.</summary>
    private static List<Diagnostic> Findings(AnalysisResult analysis, string property) =>
        analysis.Diagnostics
            .Where(d => d.Code is DomainModelDiagnosticCodes.EffectConstraintViolation
                or DomainModelDiagnosticCodes.ConstraintUndecided
                && d.Message.Contains($"'{property}'", StringComparison.Ordinal))
            .ToList();

    private static WriteVerificationFacts Facts(EvolutionResult result) =>
        result.Analysis.GetMetadata<WriteVerificationFacts>(result.Root)
        ?? throw new InvalidOperationException("no write-verification facts were recorded");

    // ── the three silent skips: #507, #651, #650 ─────────────────────────

    [Test]
    public async Task UnboundedParameterSubtraction_WarnsUnverified() {
        // #507: the RHS is a Subtract (not a ParameterAccess); the unbounded operand collapses it to
        // Unknown, which master skipped silently.
        var result = Parse("""
            domain Test
            Stock: entity {
              Reserved: Number range(0, 100000)
              Release: action (qty: Number) { assign Reserved to Reserved - qty }
            }
            """);

        var findings = Findings(result.Analysis, "Reserved");

        await Assert.That(findings.Count).IsEqualTo(1);
        await Assert.That(findings[0].Code).IsEqualTo(DomainModelDiagnosticCodes.ConstraintUndecided);
        await Assert.That(findings[0].Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(findings[0].Message).Contains("range(0, 100000)");
        var facts = Facts(result);
        await Assert.That(facts.Unverified.Count).IsEqualTo(1);
        await Assert.That(facts.Unverified[0].Reason).IsEqualTo(UnverifiedReason.UnknownBound);
        await Assert.That(facts.Verified).IsEqualTo(0);
    }

    [Test]
    public async Task UnboundedNumberProperty_InAssignExpression_WarnsUnverified() {
        // #651: U has no range, so U + 1 has none; master skipped it silently.
        var result = Parse("""
            domain Test
            Box: entity {
              A: Number range(0, 10)
              U: Number
              Run: action { assign A to U + 1 }
            }
            """);

        var findings = Findings(result.Analysis, "A");

        await Assert.That(findings.Count).IsEqualTo(1);
        await Assert.That(findings[0].Code).IsEqualTo(DomainModelDiagnosticCodes.ConstraintUndecided);
        await Assert.That(Facts(result).Unverified.Single().Reason).IsEqualTo(UnverifiedReason.UnknownBound);
    }

    [Test]
    public async Task BarePropertyRef_PartialOverlap_Warns() {
        // #650: a bare property ref never entered the range check. The value [5, 20] overlaps the
        // target [0, 10]: a known range that can fall outside is the DMEFF008 warning, not DMCS003.
        var result = Parse("""
            domain Test
            Box: entity {
              A: Number range(0, 10)
              B: Number range(5, 20)
              Run: action { assign A to B }
            }
            """);

        var findings = Findings(result.Analysis, "A");

        await Assert.That(findings.Count).IsEqualTo(1);
        await Assert.That(findings[0].Code).IsEqualTo(DomainModelDiagnosticCodes.EffectConstraintViolation);
        await Assert.That(findings[0].Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(findings[0].Message).Contains("can fall outside");
        await Assert.That(Facts(result).Unverified.Single().Reason).IsEqualTo(UnverifiedReason.PossibleViolation);
    }

    [Test]
    public async Task BarePropertyRef_ContainedRange_IsVerified() {
        // The soundness oracle: silence must mean verified, so the write is counted.
        var result = Parse("""
            domain Test
            Box: entity {
              A: Number range(0, 10)
              B: Number range(2, 8)
              Run: action { assign A to B }
            }
            """);

        await Assert.That(Findings(result.Analysis, "A")).IsEmpty();
        var facts = Facts(result);
        await Assert.That(facts.Verified).IsEqualTo(1);
        await Assert.That(facts.Unverified).IsEmpty();
        await Assert.That(facts.Violations).IsEmpty();
    }

    [Test]
    public async Task BarePropertyRef_DisjointRange_IsOneError() {
        // Programmatic: an error finding rolls a DSL evolution back, taking its facts with it.
        var entity = new Entity("Box",
            [
                new Property("A", new DomainTypeReference("Number"), [new RangeConstraint(0d, 10d)]),
                new Property("B", new DomainTypeReference("Number"), [new RangeConstraint(20d, 30d)]),
            ],
            [new DmAction("Run", InvocationResult.Void, [],
                [new AssignEffect(DomainExpression.Property("A"), DomainExpression.Property("B"))], [])],
            [],
            []);
        var domain = DomainTestFactory.Create("Test", [Number, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        var findings = Findings(analysis, "A");
        await Assert.That(findings.Count).IsEqualTo(1);
        await Assert.That(findings[0].Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(findings[0].Code).IsEqualTo(DomainModelDiagnosticCodes.EffectConstraintViolation);
        await Assert.That(analysis.GetMetadata<WriteVerificationFacts>(domain)!.Violations.Count).IsEqualTo(1);
    }

    [Test]
    public async Task BarePropertyRef_UnboundedSource_WarnsUnverified() {
        var result = Parse("""
            domain Test
            Box: entity {
              A: Number range(0, 10)
              B: Number
              Run: action { assign A to B }
            }
            """);

        var findings = Findings(result.Analysis, "A");

        await Assert.That(findings.Count).IsEqualTo(1);
        await Assert.That(findings[0].Code).IsEqualTo(DomainModelDiagnosticCodes.ConstraintUndecided);
    }

    [Test]
    public async Task DivisionIntoRangedProperty_WarnsUnverified() {
        // Division is not modelled by the interval flow: it is a named, unsupported operator.
        // (V-06 flips this to a decided write; the hand-off note lives on the unit's PR.)
        var result = Parse("""
            domain Test
            Box: entity {
              Q: Number range(0, 100)
              T: Number range(0, 10)
              Run: action { assign T to Q / 2 }
            }
            """);

        var findings = Findings(result.Analysis, "T");

        await Assert.That(findings.Count).IsEqualTo(1);
        await Assert.That(findings[0].Code).IsEqualTo(DomainModelDiagnosticCodes.ConstraintUndecided);
        await Assert.That(Facts(result).Unverified.Single().Reason).IsEqualTo(UnverifiedReason.UnsupportedOperator);
    }

    // ── the fail-closed kind default, end to end ─────────────────────────

    private sealed record UnmodelledConstraint : Constraint;

    [Test]
    public async Task NonFlowCheckedKind_Undecided_WarnsDMCS003() {
        // The DSL cannot author a kind outside the registry, so this is a programmatic domain: a
        // non-literal write into a property whose constraint kind has no flow decision.
        var entity = new Entity("Item",
            [
                new Property("Total", new DomainTypeReference("Number"), [new UnmodelledConstraint()]),
                new Property("Other", new DomainTypeReference("Number"), []),
            ],
            [new DmAction("Copy", InvocationResult.Void, [],
                [new AssignEffect(DomainExpression.Property("Total"), DomainExpression.Property("Other"))],
                [])],
            [],
            []);
        var domain = DomainTestFactory.Create("Test", [Number, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        var findings = Findings(analysis, "Total");
        await Assert.That(findings.Count).IsEqualTo(1);
        await Assert.That(findings[0].Code).IsEqualTo(DomainModelDiagnosticCodes.ConstraintUndecided);
        await Assert.That(findings[0].Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(analysis.GetMetadata<WriteVerificationFacts>(domain)!.Unverified.Single().Reason)
            .IsEqualTo(UnverifiedReason.NonFlowCheckedKind);
    }

    // ── A2: exactly one verdict per effect across stage contexts ─────────

    [Test]
    public async Task TwoStages_ViolatedInOne_UndecidedInOther_IsExactlyOneFinding() {
        // Stage A narrows Qty to [50, 100] (entirely above Total's [0, 30]: Violated); stage B narrows
        // it to [0, 40] (overlaps: Undecided). The write gets ONE verdict, the worst: the error.
        var qty = new Property("Qty", new DomainTypeReference("Number"), [new RangeConstraint(0d, 100d)]);
        var total = new Property("Total", new DomainTypeReference("Number"), [new RangeConstraint(0d, 30d)]);
        var copy = new DmAction("Copy", InvocationResult.Void, [],
            [new AssignEffect(DomainExpression.Property("Total"), DomainExpression.Property("Qty"))], []);
        var a = new Stage("A", Actions: [], Policies: [
            new Policy("A50", DomainExpression.GreaterThanOrEqual(
                DomainExpression.Property("Qty"), DomainExpression.Literal(50)))
        ], OnEntryEffects: [], OnExitEffects: []);
        var b = new Stage("B", Actions: [], Policies: [
            new Policy("B40", DomainExpression.LessThanOrEqual(
                DomainExpression.Property("Qty"), DomainExpression.Literal(40)))
        ], OnEntryEffects: [], OnExitEffects: []);
        var entity = new Entity("Item", [qty, total], [copy], [], [a, b]);
        var domain = DomainTestFactory.Create("Test", [Number, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.GetActionInvariants(copy)!.StageContexts().Count).IsEqualTo(2);
        var findings = Findings(analysis, "Total");
        await Assert.That(findings.Count).IsEqualTo(1);
        await Assert.That(findings[0].Severity).IsEqualTo(DiagnosticSeverity.Error);
        var facts = analysis.GetMetadata<WriteVerificationFacts>(domain)!;
        await Assert.That(facts.Violations.Count).IsEqualTo(1);
        await Assert.That(facts.Unverified).IsEmpty();
        await Assert.That(facts.Verified).IsEqualTo(0);
    }

    [Test]
    public async Task TwoStages_UndecidedInOne_SatisfiedInOther_IsNotVerified() {
        // A write is verified only if EVERY stage context is Satisfied.
        var qty = new Property("Qty", new DomainTypeReference("Number"), [new RangeConstraint(0d, 100d)]);
        var total = new Property("Total", new DomainTypeReference("Number"), [new RangeConstraint(0d, 30d)]);
        var copy = new DmAction("Copy", InvocationResult.Void, [],
            [new AssignEffect(DomainExpression.Property("Total"), DomainExpression.Property("Qty"))], []);
        var a = new Stage("A", Actions: [], Policies: [
            new Policy("A20", DomainExpression.LessThanOrEqual(
                DomainExpression.Property("Qty"), DomainExpression.Literal(20)))
        ], OnEntryEffects: [], OnExitEffects: []);
        var b = new Stage("B", Actions: [], Policies: [], OnEntryEffects: [], OnExitEffects: []);
        var entity = new Entity("Item", [qty, total], [copy], [], [a, b]);
        var domain = DomainTestFactory.Create("Test", [Number, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(Findings(analysis, "Total").Count).IsEqualTo(1);
        var facts = analysis.GetMetadata<WriteVerificationFacts>(domain)!;
        await Assert.That(facts.Unverified.Count).IsEqualTo(1);
        await Assert.That(facts.Verified).IsEqualTo(0);
    }

    // ── A3: one recorder, one fact per write ─────────────────────────────

    [Test]
    public async Task BareParameter_NoRange_IsExactlyOneFinding() {
        var result = Parse("""
            domain Test
            Stock: entity {
              Reserved: Number range(0, 100)
              Set: action (qty: Number) { assign Reserved to qty }
            }
            """);

        var findings = Findings(result.Analysis, "Reserved");

        await Assert.That(findings.Count).IsEqualTo(1);
        var facts = Facts(result);
        await Assert.That(facts.Unverified.Count).IsEqualTo(1);
        await Assert.That(facts.Unverified[0].Reason).IsEqualTo(UnverifiedReason.UnknownBound);
        await Assert.That(facts.Verified).IsEqualTo(0);
    }

    // A parameter's range cannot be authored in the DSL, so these two build the domain directly.
    private static (Domain Domain, AnalysisResult Analysis) SetFromParameter(double paramMax) {
        var entity = new Entity("Stock",
            [new Property("Reserved", new DomainTypeReference("Number"), [new RangeConstraint(0d, 100d)])],
            [new DmAction("Set", InvocationResult.Void,
                [new Property("qty", new DomainTypeReference("Number"), [new RangeConstraint(0d, paramMax)])],
                [new AssignEffect(DomainExpression.Property("Reserved"), DomainExpression.Parameter("qty"))],
                [])],
            [],
            []);
        var domain = DomainTestFactory.Create("Test", [Number, entity], []);
        return (domain, DomainModelAnalyzer.Analyze(domain));
    }

    [Test]
    public async Task BareParameter_WiderRange_IsOneFindingAndPossibleViolation() {
        var (domain, analysis) = SetFromParameter(500);

        await Assert.That(Findings(analysis, "Reserved").Count).IsEqualTo(1);
        await Assert.That(analysis.GetMetadata<WriteVerificationFacts>(domain)!.Unverified.Single().Reason)
            .IsEqualTo(UnverifiedReason.PossibleViolation);
    }

    [Test]
    public async Task BareParameter_SubsumedRange_IsVerified() {
        var (domain, analysis) = SetFromParameter(50);

        await Assert.That(Findings(analysis, "Reserved")).IsEmpty();
        var facts = analysis.GetMetadata<WriteVerificationFacts>(domain)!;
        await Assert.That(facts.Verified).IsEqualTo(1);
        await Assert.That(facts.Unverified).IsEmpty();
    }

    [Test]
    public async Task LiteralWrites_AreRecordedOncePerWrite() {
        var entity = new Entity("Stock",
            [new Property("Reserved", new DomainTypeReference("Number"), [new RangeConstraint(0d, 100d)])],
            [
                new DmAction("Ok", InvocationResult.Void, [],
                    [new AssignEffect(DomainExpression.Property("Reserved"), DomainExpression.Literal(5))], []),
                new DmAction("Bad", InvocationResult.Void, [],
                    [new AssignEffect(DomainExpression.Property("Reserved"), DomainExpression.Literal(500))], []),
            ],
            [],
            []);
        var domain = DomainTestFactory.Create("Test", [Number, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        var facts = analysis.GetMetadata<WriteVerificationFacts>(domain)!;
        await Assert.That(facts.Verified).IsEqualTo(1);
        await Assert.That(facts.Violations.Count).IsEqualTo(1);
        await Assert.That(facts.Unverified).IsEmpty();
        await Assert.That(Findings(analysis, "Reserved").Count).IsEqualTo(1);
    }

    [Test]
    public async Task LiteralWrite_ToUnmodelledKind_IsOneUnverifiedWrite() {
        // The literal path already warned DMCS003 but recorded nothing: V-04 must not report Proven.
        var entity = new Entity("Item",
            [new Property("Total", new DomainTypeReference("Number"), [new UnmodelledConstraint()])],
            [new DmAction("SetTotal", InvocationResult.Void, [],
                [new AssignEffect(DomainExpression.Property("Total"), DomainExpression.Literal(200))],
                [])],
            [],
            []);
        var domain = DomainTestFactory.Create("Test", [Number, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(Findings(analysis, "Total").Count).IsEqualTo(1);
        var facts = analysis.GetMetadata<WriteVerificationFacts>(domain)!;
        await Assert.That(facts.Unverified.Single().Reason).IsEqualTo(UnverifiedReason.NonFlowCheckedKind);
    }

    // ── S4: create initializers on the same channel ──────────────────────

    [Test]
    public async Task CreateInitializer_BareRefIntoRangedProperty_Warns() {
        // A bare property reference in a create initializer was skipped on master (the gate only
        // accepted literals and arithmetic). The creator's Amount has no range, so the value is
        // Unknown and the write is DMCS003, never silence.
        var result = Parse("""
            domain Test
            Source: entity {
              Amount: Number
              Make: action { create Item { Amount: Amount } }
            }
            Item: entity {
              Amount: Number range(0, 10)
            }
            """);

        var findings = Findings(result.Analysis, "Amount");

        await Assert.That(findings.Count).IsEqualTo(1);
        await Assert.That(findings[0].Code).IsEqualTo(DomainModelDiagnosticCodes.ConstraintUndecided);
        await Assert.That(findings[0].Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(Facts(result).Unverified.Single().Reason).IsEqualTo(UnverifiedReason.UnknownBound);
    }

    [Test]
    public async Task CreateInitializer_IsEvaluatedInTheCreatorsScope_NotTheTargets() {
        // The target has a property of the same name with a different range. Creator [0, 10] against
        // target [100, 200] is entirely below: a definite violation. Read in the target's scope the
        // value would be [100, 200] and the write would be silently verified (A1).
        var result = Parse("""
            domain Test
            Creator: entity {
              Amount: Number range(0, 10)
              Make: action { create Item { Amount: Amount } }
            }
            Item: entity {
              Amount: Number range(100, 200)
            }
            """);

        var findings = Findings(result.Analysis, "Amount");

        await Assert.That(findings.Count).IsEqualTo(1);
        await Assert.That(findings[0].Code).IsEqualTo(DomainModelDiagnosticCodes.EffectConstraintViolation);
        await Assert.That(findings[0].Severity).IsEqualTo(DiagnosticSeverity.Error);
        // The error rolls the DSL evolution back, taking the recorded facts with it; the violation is
        // read from the diagnostic. (Programmatic domains keep their facts: BarePropertyRef_DisjointRange_IsOneError.)
        await Assert.That(result.Succeeded).IsFalse();
    }

    [Test]
    public async Task CreateInitializer_RangedParameter_IsVerified() {
        // A ranged parameter cannot be authored in the DSL, so this builds the domain directly: the
        // parameter [0, 50] is inside the target [0, 100], so the initializer is verified and silent.
        var item = new Entity("Item",
            [new Property("Qty", new DomainTypeReference("Number"), [new RangeConstraint(0d, 100d)])],
            [],
            [],
            []);
        var creator = new Entity("Creator",
            [],
            [new DmAction("Make", InvocationResult.Void,
                [new Property("qty", new DomainTypeReference("Number"), [new RangeConstraint(0d, 50d)])],
                [
                    new CreateEntityInstance(
                        new DomainTypeReference("Item"),
                        [new PropertyBinding("Qty", DomainExpression.Parameter("qty"))]),
                ],
                [])],
            [],
            []);
        var domain = DomainTestFactory.Create("Test", [Number, creator, item], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(Findings(analysis, "Qty")).IsEmpty();
        var facts = analysis.GetMetadata<WriteVerificationFacts>(domain);
        await Assert.That(facts).IsNotNull();
        await Assert.That(facts!.Verified).IsEqualTo(1);
        await Assert.That(facts.Unverified).IsEmpty();
        await Assert.That(facts.Violations).IsEmpty();
    }

    [Test]
    public async Task CreateInitializer_Write_IsExactlyOneFinding() {
        // A ranged parameter wider than the target: one DMCS003-style warning ("can fall outside") and
        // exactly one recorded write, never one per constraint or per stage context.
        var item = new Entity("Item",
            [new Property("Qty", new DomainTypeReference("Number"), [new RangeConstraint(0d, 100d)])],
            [],
            [],
            []);
        var creator = new Entity("Creator",
            [],
            [new DmAction("Make", InvocationResult.Void,
                [new Property("qty", new DomainTypeReference("Number"), [new RangeConstraint(0d, 500d)])],
                [
                    new CreateEntityInstance(
                        new DomainTypeReference("Item"),
                        [new PropertyBinding("Qty", DomainExpression.Parameter("qty"))]),
                ],
                [])],
            [],
            []);
        var domain = DomainTestFactory.Create("Test", [Number, creator, item], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        var findings = Findings(analysis, "Qty");
        await Assert.That(findings.Count).IsEqualTo(1);
        await Assert.That(findings[0].Code).IsEqualTo(DomainModelDiagnosticCodes.EffectConstraintViolation);
        await Assert.That(findings[0].Severity).IsEqualTo(DiagnosticSeverity.Warning);
        var facts = analysis.GetMetadata<WriteVerificationFacts>(domain);
        await Assert.That(facts).IsNotNull();
        await Assert.That(facts!.Unverified.Count).IsEqualTo(1);
        await Assert.That(facts.Unverified[0].Reason).IsEqualTo(UnverifiedReason.PossibleViolation);
    }

    // ── effects outside an action body ───────────────────────────────────

    [Test]
    public async Task StageEntryEffect_BareRef_GetsAVerdict() {
        // An entry effect has no action, so no postcondition: the value is inferred in place and
        // still gets exactly one verdict.
        var a = new Property("A", new DomainTypeReference("Number"), [new RangeConstraint(0d, 10d)]);
        var b = new Property("B", new DomainTypeReference("Number"), [new RangeConstraint(5d, 20d)]);
        var stage = new Stage("Open", Actions: [], Policies: [],
            OnEntryEffects: [new AssignEffect(DomainExpression.Property("A"), DomainExpression.Property("B"))],
            OnExitEffects: []);
        var entity = new Entity("Item", [a, b], [], [], [stage]);
        var domain = DomainTestFactory.Create("Test", [Number, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(Findings(analysis, "A").Count).IsEqualTo(1);
        await Assert.That(analysis.GetMetadata<WriteVerificationFacts>(domain)!.Unverified.Count).IsEqualTo(1);
    }

    // ── fix round 1: tie-break determinism, nested operators, the parameter arm ──

    /// <summary>The reported code for one write that is PossibleViolation in one stage and UnknownBound in the other.</summary>
    private static string TwoStageVerdictCode(bool narrowedStageFirst) {
        // Qty has no declared range. Stage "Narrowed" narrows it to (-∞, 100] with a policy, so the
        // write has a known range that overlaps Total's [0, 10]: PossibleViolation. Stage "Open" has
        // no policy, so Qty stays unbounded: UnknownBound. Both are Unverified — the tie-break must
        // not be stage order.
        var qty = new Property("Qty", new DomainTypeReference("Number"), []);
        var total = new Property("Total", new DomainTypeReference("Number"), [new RangeConstraint(0d, 10d)]);
        var copy = new DmAction("Copy", InvocationResult.Void, [],
            [new AssignEffect(DomainExpression.Property("Total"), DomainExpression.Property("Qty"))], []);
        Stage Narrowed() => new("Narrowed", Actions: [], Policies: [
            new Policy("Under100", DomainExpression.LessThanOrEqual(
                DomainExpression.Property("Qty"), DomainExpression.Literal(100)))
        ], OnEntryEffects: [], OnExitEffects: []);
        Stage Open() => new("Open", Actions: [], Policies: [], OnEntryEffects: [], OnExitEffects: []);
        var stages = narrowedStageFirst ? new[] { Narrowed(), Open() } : new[] { Open(), Narrowed() };
        var entity = new Entity("Item", [qty, total], [copy], [], stages);
        var domain = DomainTestFactory.Create("Test", [Number, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        return Findings(analysis, "Total").Single().Code!;
    }

    [Test]
    public async Task TwoStages_PossibleViolationAndUnknownBound_ReportTheSameCodeInEitherOrder() {
        // The worst reason wins deterministically (UnknownBound, DMCS003), so swapping the stages
        // cannot flip the reported code from DMCS003 to DMEFF008 and back.
        await Assert.That(TwoStageVerdictCode(narrowedStageFirst: true))
            .IsEqualTo(TwoStageVerdictCode(narrowedStageFirst: false));
        await Assert.That(TwoStageVerdictCode(narrowedStageFirst: true))
            .IsEqualTo(DomainModelDiagnosticCodes.ConstraintUndecided);
    }

    [Test]
    public async Task NestedUnmodelledOperator_ReasonIsUnsupportedOperator() {
        // Division is nested inside an Add: the reason is the operator analysis does not model, not
        // "UnknownBound" from the top-level Add.
        var result = Parse("""
            domain Test
            Box: entity {
              Q: Number range(0, 100)
              T: Number range(0, 10)
              Run: action { assign T to Q / 2 + 1 }
            }
            """);

        var findings = Findings(result.Analysis, "T");

        await Assert.That(findings.Count).IsEqualTo(1);
        await Assert.That(findings[0].Code).IsEqualTo(DomainModelDiagnosticCodes.ConstraintUndecided);
        await Assert.That(Facts(result).Unverified.Single().Reason).IsEqualTo(UnverifiedReason.UnsupportedOperator);
    }

    [Test]
    public async Task ParameterArm_NoRangeParameter_ReportsOnceThroughTheDeclaredArm() {
        // A parameter cannot be given a range in the DSL, so this is a programmatic domain.
        // ConstraintPropagationAnalyzer ALSO publishes the target's own range for the parameter
        // (CollectFromAssign), so the downstream arm runs too: a count of one proves only the
        // declared arm reported, and the message names the parameter's own constraint (review H7).
        var entity = new Entity("Stock",
            [new Property("Reserved", new DomainTypeReference("Number"), [new RangeConstraint(0d, 100d)])],
            [new DmAction("Set", InvocationResult.Void,
                [new Property("qty", new DomainTypeReference("Number"), [])],
                [new AssignEffect(DomainExpression.Property("Reserved"), DomainExpression.Parameter("qty"))],
                [])],
            [],
            []);
        var domain = DomainTestFactory.Create("Test", [Number, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        var findings = Findings(analysis, "Reserved");
        await Assert.That(findings.Count).IsEqualTo(1);
        await Assert.That(findings[0].Code).IsEqualTo(DomainModelDiagnosticCodes.EffectConstraintViolation);
        await Assert.That(findings[0].Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(findings[0].Message).Contains("has no range(0, 100) constraint");
        await Assert.That(findings[0].Message).DoesNotContain("Effect-chain constraint");
        await Assert.That(analysis.GetMetadata<WriteVerificationFacts>(domain)!.Unverified.Count).IsEqualTo(1);
    }
}