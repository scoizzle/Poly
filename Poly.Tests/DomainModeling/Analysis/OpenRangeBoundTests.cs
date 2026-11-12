using Microsoft.Extensions.Logging.Abstractions;

using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.Mcp.Surface;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// #475 / V-01: an absent <c>range</c> bound is unbounded, never 0. <c>Convert.ToDouble(null)</c>
/// is 0, so an open side collapsed to 0 in three places at once: the bound read itself
/// (<see cref="AbstractValue.NumericRange"/>), arithmetic composition, and the derived/call-chain
/// range checks. <c>range(0, )</c> with <c>assign D to D + 1</c> was reported as *entirely
/// outside*, rejecting the plain counter. Open bounds now stay absent through the whole path;
/// <c>*</c> on an open operand deliberately stays fail-open (Unknown) and is pinned as such.
/// </summary>
public class OpenRangeBoundTests {
    private static EvolutionResult Parse(string dsl) {
        var changes = new PolyDslParser(dsl).Parse();
        return new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
    }

    private static List<Poly.Analysis.Diagnostic> EffectViolations(EvolutionResult result) =>
        result.Analysis.Diagnostics
            .Where(d => d.Code == DomainModelDiagnosticCodes.EffectConstraintViolation)
            .ToList();

    private static List<Poly.Analysis.Diagnostic> Undecided(EvolutionResult result) =>
        result.Analysis.Diagnostics
            .Where(d => d.Code == DomainModelDiagnosticCodes.ConstraintUndecided)
            .ToList();

    private static WriteVerificationFacts Facts(EvolutionResult result) =>
        result.Analysis.GetMetadata<WriteVerificationFacts>(result.Root)
        ?? throw new InvalidOperationException("no write-verification facts were recorded");

    // ── B1: an open bound is absent, not 0 ───────────────────────────────

    [Test]
    public async Task NumericRange_OpenUpperBound_IsNullNotZero() {
        var range = AbstractValue.From([new RangeConstraint(0, null)]).NumericRange;

        await Assert.That(range).IsNotNull();
        await Assert.That(range!.Min).IsEqualTo(0m);
        await Assert.That(range.Max).IsNull();
    }

    // ── B2: + / - carry an open endpoint ─────────────────────────────────

    [Test]
    public async Task Assign_AddToOpenUpperBound_ReportsNoDMEFF008() {
        // [0, +∞) + 1 is [1, +∞), inside range(0, +∞). Red on master: the open max reads 0,
        // so the value is [1, 1] and the target max 0 puts it entirely above (error).
        var result = Parse("""
            domain Test
            A: entity {
              D: Number range(0, )
              Dep: action { assign D to D + 1 }
            }
            """);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(EffectViolations(result)).IsEmpty();
    }

    [Test]
    public async Task Assign_SubtractFromOpenLowerBound_ReportsNoDMEFF008() {
        // (-∞, 100] - 1 is (-∞, 99], inside range(, 100). Red on master: the open min reads 0,
        // so the value is [-1, 99] and the target min 0 makes it a warning.
        var result = Parse("""
            domain Test
            A: entity {
              D: Number range(, 100)
              Dep: action { assign D to D - 1 }
            }
            """);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(EffectViolations(result)).IsEmpty();
    }

    [Test]
    public async Task Assign_SubtractFromOpenUpperBound_WarnsCanFallOutside() {
        // [0, +∞) - 1 is [-1, +∞): a possible violation of the lower bound, never a definite one.
        // Red on master: the open max reads 0, so the value is [-1, -1] and it is an error.
        var result = Parse("""
            domain Test
            A: entity {
              D: Number range(0, )
              Dep: action { assign D to D - 1 }
            }
            """);

        var violations = EffectViolations(result);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(violations.Count).IsEqualTo(1);
        await Assert.That(violations[0].Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(violations[0].Message).Contains("∞");
    }

    [Test]
    public async Task Assign_AddToOpenUpperBoundWithNegativeLower_ReportsNoDMEFF008() {
        // [-5, +∞) + 1 is [-4, +∞), inside range(-5, +∞). Red on master: the open max reads 0,
        // so the value is [-4, 1] and the target max 0 makes it a warning (issue variant 3).
        var result = Parse("""
            domain Test
            A: entity {
              D: Number range(-5, )
              Dep: action { assign D to D + 1 }
            }
            """);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(EffectViolations(result)).IsEmpty();
    }

    // ── B4: the call-chain path never dereferences an absent bound ───────

    [Test]
    public async Task CallChainPostcondition_SubtractFromOpenUpperBound_WarnsNotErrors() {
        // Red on master: the callee value is [-1, -1] against target min 0, so the caller's
        // postcondition is an error. Filtered to the caller chain, as ConstraintFailClosedAuditTests
        // does, because Set's own direct assign reports too and must not stand in for the chain.
        var result = Parse("""
            domain Test
            A: entity {
              D: Number range(0, )
              Set: action { assign D to D - 1 }
              Go: action { invoke Set }
            }
            """);

        var chain = result.Analysis.Diagnostics
            .Where(d => d.Message.Contains("Call-chain postcondition (Go → Set)", StringComparison.Ordinal))
            .ToList();

        await Assert.That(chain.Any(d => d.Severity == DiagnosticSeverity.Error)).IsFalse();
        await Assert.That(chain.Any(d => d.Severity == DiagnosticSeverity.Warning)).IsTrue();
    }

    // ── B5: the storage envelope stops certifying a cross-property open write

    [Test]
    public async Task Storage_ClosedTarget_WrittenFromOpenProperty_IsNotRangeVerified() {
        // Total may receive 10^9 from Qty, so a CHECK built from range(0, 100) would false-reject.
        // Red on master: the written side reads [0, 0] and is certified.
        var result = Parse("""
            domain Test
            Item: entity {
              Total: Number range(0, 100)
              Qty: Number range(0, )
              Dep: action { assign Total to Qty }
            }
            """);

        var topology = EffectTopologyPass.Scan(result.Root);
        var aggregate = OwnershipAggregatePass.BuildAggregate(result.Root, null, topology);
        var storage = new StorageAnalyzer(result.Root, result.Analysis).Analyze(aggregate, topology);
        var column = storage.Entities[0].Columns.Single(c => c.Name == "Total");

        await Assert.That(column.IsRangeVerified).IsFalse();
    }

    // ── B6: the authoring door ───────────────────────────────────────────

    [Test]
    public async Task Define_OpenRangeIncrement_IsAcceptedWithNoDmeff008() {
        // The verbatim #475 repro through the in-process surface. Red on master: the error makes
        // Define reject the document.
        var surface = new PolySurfaceTools(McpTestTools.Store, NullLogger<PolySurfaceTools>.Instance);
        var workspace = surface.Workspace(name: "OpenRangeBound").State!.Workspace;

        var response = surface.Define(workspace, poly: """
            domain T
            A: entity {
              D: Number range(0, )
              Dep: action { assign D to D + 1 }
            }
            """);

        await Assert.That(response.Ok).IsTrue();
        await Assert.That(response.Findings.Any(f => f.Code == "DMEFF008")).IsFalse();
    }

    // ── Pins: green before and after ─────────────────────────────────────

    [Test]
    public async Task Assign_AddToClosedRangeEntirelyAbove_StillReportsDMEFF008() {
        // Pin: a closed value entirely above a closed target stays a definite violation.
        var result = Parse("""
            domain Test
            A: entity {
              D: Number range(0, 10)
              Dep: action { assign D to D + 500 }
            }
            """);

        var violations = EffectViolations(result);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(violations.Any(d => d.Severity == DiagnosticSeverity.Error)).IsTrue();
    }

    [Test]
    public async Task Assign_MultiplyOpenOperand_ReportsNoDMEFF008() {
        // Pin: `*` on an open operand stays fail-open (no interval), so there is no DMEFF008.
        // Q4 (V-03): silence is reserved for verified writes, so the write is DMCS003 instead.
        var result = Parse("""
            domain Test
            A: entity {
              D: Number range(0, )
              Dep: action { assign D to D * 2 }
            }
            """);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(EffectViolations(result)).IsEmpty();
        await Assert.That(Undecided(result).Count).IsEqualTo(1);
        await Assert.That(Facts(result).Unverified.Single().Reason).IsEqualTo(UnverifiedReason.UnknownBound);
    }

    [Test]
    public async Task CreateInitializer_UnknownArithmetic_ReportsNoDMEFF008() {
        // Pin (O1): an Unknown arithmetic initializer carries no range at all (both sides absent),
        // not an open one, so it is not compared against the target's declared range. Green on
        // master via the both-null return, and red under a naive ±∞ substitution. Q4 (S4): the
        // initializer now goes through the write channel, so it is DMCS003, never silent.
        var result = Parse("""
            domain Test
            A: entity { Qty: Number range(0, 100) }
            B: entity {
              Make: action (x: Number) {
                create A { Qty: x + 1 }
              }
            }
            """);

        await Assert.That(EffectViolations(result)).IsEmpty();
        var undecided = Undecided(result);
        await Assert.That(undecided.Count).IsEqualTo(1);
        await Assert.That(undecided[0].Message).Contains("'Qty'");
        await Assert.That(Facts(result).Unverified.Single().Reason).IsEqualTo(UnverifiedReason.UnknownBound);
    }

    [Test]
    public async Task CallChainPostcondition_OpenBound_DoesNotThrow() {
        // Pin, not red: on master vr.Max reads as 0, so nothing throws; at the intermediate state
        // (B1 landed, B4 not) Nullable<T>.Value on the absent bound throws
        // InvalidOperationException — green-on-master here is not evidence B4 already worked.
        var result = Parse("""
            domain Test
            A: entity {
              D: Number range(0, )
              Set: action { assign D to D }
              Go: action { invoke Set }
            }
            """);

        await Assert.That(EffectViolations(result).Any(d => d.Severity == DiagnosticSeverity.Error)).IsFalse();
    }

    // ── V-03 S6: the rest of V-01's follow-ups ───────────────────────────
    // Everything in this section is a PIN, not a red: it passes on master (V-01) and after V-03.
    // The one expectation Q4 makes wrong is the old "pin today's silence" for A - B, replaced
    // below by a DMCS003 assertion: under Q4 silence means verified, and two open operands are
    // not verified.

    [Test]
    public async Task TwoOpenOperands_IsOneUnverifiedWrite() {
        // `A - B` with both operands open: the difference has no bound at all, so nothing is known
        // outside the target's range. Q4 makes the old "today's silence" expectation wrong — the
        // write is DMCS003 (Unverified, UnknownBound), exactly one finding and one fact.
        var result = Parse("""
            domain Test
            A: entity {
              X: Number range(0, )
              Y: Number range(0, )
              Dep: action { assign X to X - Y }
            }
            """);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(EffectViolations(result)).IsEmpty();
        var undecided = Undecided(result);
        await Assert.That(undecided.Count).IsEqualTo(1);
        await Assert.That(undecided[0].Severity).IsEqualTo(DiagnosticSeverity.Warning);
        var facts = Facts(result);
        await Assert.That(facts.Unverified.Single().Reason).IsEqualTo(UnverifiedReason.UnknownBound);
        await Assert.That(facts.Verified).IsEqualTo(0);
    }

    [Test]
    public async Task OpenOperandShapes_AreRecordedAsUnverifiedWrites() {
        // The mixed-side, `*` and bare-copy shapes V-01 pinned as silent are recorded, one write
        // each: two have no bound at all (DMCS003), one is a known range that can fall outside
        // (DMEFF008 warning). None of them is a verified write.
        var result = Parse("""
            domain Test
            A: entity {
              Open: Number range(0, )
              Open2: Number range(0, )
              Closed: Number range(0, 100)
              Difference: action { assign Closed to Open - Open2 }
              Scaled: action { assign Closed to Open * 2 }
              Copy: action { assign Closed to Open }
            }
            """);

        await Assert.That(result.Succeeded).IsTrue();
        var findings = result.Analysis.Diagnostics
            .Where(d => d.Message.Contains("'Closed'", StringComparison.Ordinal)
                && d.Code is DomainModelDiagnosticCodes.ConstraintUndecided
                    or DomainModelDiagnosticCodes.EffectConstraintViolation)
            .ToList();
        await Assert.That(findings.Count).IsEqualTo(3);
        await Assert.That(Undecided(result).Count).IsEqualTo(2);
        var facts = Facts(result);
        await Assert.That(facts.Unverified.Count).IsEqualTo(3);
        await Assert.That(facts.Verified).IsEqualTo(0);
    }

    [Test]
    public async Task CallChainPostcondition_AddToOpenLowerBound_WarnsNotErrors() {
        // (-∞, 100] + 1 is (-∞, 101]: above the target's upper bound, but the lower side is open, so
        // it can fall outside — a warning on the caller chain, never an error. The counterpart of
        // CallChainPostcondition_SubtractFromOpenUpperBound_WarnsNotErrors.
        var result = Parse("""
            domain Test
            A: entity {
              D: Number range(, 100)
              Set: action { assign D to D + 1 }
              Go: action { invoke Set }
            }
            """);

        var chain = result.Analysis.Diagnostics
            .Where(d => d.Message.Contains("Call-chain postcondition (Go → Set)", StringComparison.Ordinal))
            .ToList();

        await Assert.That(chain.Any(d => d.Severity == DiagnosticSeverity.Error)).IsFalse();
        await Assert.That(chain.Any(d => d.Severity == DiagnosticSeverity.Warning)).IsTrue();
    }

    [Test]
    public async Task Assign_OpenValueLowerEnd_WarnsWithMinusInfinity() {
        // (-∞, 50] + 0 into [0, 100]: the value's lower endpoint is absent, so it can fall outside
        // and the message names that side −∞ rather than guessing a number.
        var result = Parse("""
            domain Test
            A: entity {
              D: Number range(0, 100)
              E: Number range(, 50)
              Dep: action { assign D to E + 0 }
            }
            """);

        var warnings = EffectViolations(result);
        await Assert.That(warnings.Count).IsEqualTo(1);
        await Assert.That(warnings[0].Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(warnings[0].Message).Contains("−∞");
    }

    [Test]
    public async Task Assign_AddToOpenUpperEnd_WarnsNotErrors() {
        // [0, +∞) + 1 = [1, +∞) against [0, 100]: the upper side is open, so it can fall outside —
        // a warning, never a definite violation.
        var result = Parse("""
            domain Test
            A: entity {
              D: Number range(0, 100)
              E: Number range(0, )
              Dep: action { assign D to E + 1 }
            }
            """);

        var warnings = EffectViolations(result);
        await Assert.That(warnings.Count).IsEqualTo(1);
        await Assert.That(warnings[0].Severity).IsEqualTo(DiagnosticSeverity.Warning);
    }
}