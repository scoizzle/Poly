using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// Slice B oracle: Lower at Lower — execute never lowers.
/// Interpreter runs cached module / EntryExit / segment / policy bodies only.
/// </summary>
public class SliceBLowerAtLowerTests {
    [Test]
    public async Task SliceB_NestedStageTransition_OnEntry_BindsCachedSegments() {
        var (domain, analysis, session) = Evolve("""
            domain Flow
            Ticket: entity {
              Note: Text
              Log: Text
              Draft: stage { }
              Mid: stage {
                entry {
                  assign Note to "mid-before"
                  transition to Active
                  assign Log to "mid-after"
                }
              }
              Active: stage {
                entry { assign Note to "active" }
              }
            }
            """);

        var module = session.Lower(domain, analysis);
        await Assert.That(module.Count).IsGreaterThan(0);

        await Assert.That(RuntimeAnalysisCache.TryGetEntryExitSegmentBody(
            domain, "Ticket", "Mid", "entry", 0, out var seg0)).IsTrue();
        await Assert.That(seg0).IsNotNull();
        await Assert.That(RuntimeAnalysisCache.TryGetEntryExitSegmentBody(
            domain, "Ticket", "Mid", "entry", 1, out var seg1)).IsTrue();
        await Assert.That(seg1).IsNotNull();

        var ticketE = domain.Types.OfType<Entity>().First(e => e.Name == "Ticket");
        var inst = DomainEntityInstance.Create(ticketE,
            new Dictionary<string, object?> { ["Note"] = "", ["Log"] = "" }, domain);
        await Assert.That(inst.CurrentStage).IsEqualTo("Draft");

        inst.TransitionStage("Mid");
        await Assert.That(inst.CurrentStage).IsEqualTo("Active");
        await Assert.That(inst.GetProperty<string>("Note")).IsEqualTo("active");
        await Assert.That(inst.GetProperty<string>("Log")).IsEqualTo("mid-after");
    }

    [Test]
    public async Task SliceB_EvaluatePolicy_UsesCachedBody_ClearThrowsFailClosed() {
        var (domain, analysis, session) = Evolve("""
            domain Parking
            Permit: entity {
              Age: Number
              Adult: policy { Age >= 18 }
            }
            """);
        _ = session.Lower(domain, analysis);

        var permitE = domain.Types.OfType<Entity>().First(e => e.Name == "Permit");
        var adult = DomainEntityInstance.Create(permitE,
            new Dictionary<string, object?> { ["Age"] = 25L }, domain);
        var policy = permitE.Policies.First(p => p.Name == "Adult");
        await Assert.That(adult.EvaluatePolicy(policy)).IsTrue();

        RuntimeAnalysisCache.ClearPolicyBody(domain, "Permit", "Adult");
        var ex = Assert.Throws<InvalidOperationException>(() => adult.EvaluatePolicy(policy));
        await Assert.That(ex!.Message).Contains("missing");
    }

    [Test]
    public async Task SliceB_EvaluatePolicy_DomainNull_ThrowsFailClosed() {
        var age = new Property("Age", new DomainTypeReference("Number"), []);
        var policy = new Policy("Adult",
            DomainExpression.GreaterThanOrEqual(
                DomainExpression.Property("Age"),
                DomainExpression.Literal(18L)));
        var entity = new Entity("Person", [age], [], [policy], []);
        var inst = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Age"] = 25L });
        await Assert.That(inst.Domain).IsNull();
        var ex = Assert.Throws<InvalidOperationException>(() => inst.EvaluatePolicy(policy));
        await Assert.That(ex!.Message).Contains("without a Domain-bound module");
    }

    [Test]
    public async Task SliceB_ClearEntryExitSegment_ThrowsFailClosed_NoRelower() {
        var (domain, analysis, session) = Evolve("""
            domain Flow
            Ticket: entity {
              Note: Text
              Log: Text
              Draft: stage { }
              Mid: stage {
                entry {
                  assign Note to "before"
                  transition to Active
                  assign Log to "after"
                }
              }
              Active: stage { }
            }
            """);
        _ = session.Lower(domain, analysis);

        RuntimeAnalysisCache.ClearEntryExitSegmentBody(domain, "Ticket", "Mid", "entry", 0);

        var ticketE = domain.Types.OfType<Entity>().First(e => e.Name == "Ticket");
        var inst = DomainEntityInstance.Create(ticketE,
            new Dictionary<string, object?> { ["Note"] = "", ["Log"] = "" }, domain);

        var ex = Assert.Throws<InvalidOperationException>(() => inst.TransitionStage("Mid"));
        await Assert.That(ex!.Message).Contains("segment");
    }

    [Test]
    public async Task SliceB_NamedAction_StillRunsFromCachedModule() {
        var (domain, analysis, session) = Evolve("""
            domain Parking
            Lot: entity {
              Tag: Text
              Mark: action {
                assign Tag to "ok"
              }
            }
            """);
        var module = session.Lower(domain, analysis);
        var lotType = module.First(t => t.Name == "Lot");
        var mark = lotType.Methods?.FirstOrDefault(m => m.Name == "Mark");
        await Assert.That(mark?.Body).IsNotNull();

        var lotE = domain.Types.OfType<Entity>().First(e => e.Name == "Lot");
        var lot = DomainEntityInstance.Create(lotE,
            new Dictionary<string, object?> { ["Tag"] = "" }, domain);
        var result = lot.InvokeAction("Mark");
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(lot.GetProperty<string>("Tag")).IsEqualTo("ok");
        await Assert.That(RuntimeAnalysisCache.TryGetModuleMethod(domain, "Lot", "Mark", out var cached)).IsTrue();
        await Assert.That(ReferenceEquals(mark, cached)).IsTrue();
    }

    [Test]
    public async Task SliceB_ClearEntryExitSegment1_ThrowsFailClosed_NoRelower() {
        var (domain, analysis, session) = Evolve("""
            domain Flow
            Ticket: entity {
              Note: Text
              Log: Text
              Draft: stage { }
              Mid: stage {
                entry {
                  assign Note to "before"
                  transition to Active
                  assign Log to "after"
                }
              }
              Active: stage { }
            }
            """);
        _ = session.Lower(domain, analysis);

        await Assert.That(RuntimeAnalysisCache.TryGetEntryExitSegmentBody(
            domain, "Ticket", "Mid", "entry", 1, out _)).IsTrue();
        RuntimeAnalysisCache.ClearEntryExitSegmentBody(domain, "Ticket", "Mid", "entry", 1);

        var ticketE = domain.Types.OfType<Entity>().First(e => e.Name == "Ticket");
        var inst = DomainEntityInstance.Create(ticketE,
            new Dictionary<string, object?> { ["Note"] = "", ["Log"] = "" }, domain);

        var ex = Assert.Throws<InvalidOperationException>(() => inst.TransitionStage("Mid"));
        await Assert.That(ex!.Message).Contains("segment");
        await Assert.That(ex.Message).Contains("[1]");
    }

    [Test]
    public async Task SliceB_TransitionStage_DomainNull_NonStEntry_ThrowsFailClosed() {
        var note = new Property("Note", new DomainTypeReference("Text"), []);
        var draft = new Stage("Draft", Actions: [], Policies: [], OnEntryEffects: [], OnExitEffects: []);
        var active = new Stage(
            "Active",
            Actions: [],
            Policies: [],
            OnEntryEffects: [
                new AssignEffect(
                    DomainExpression.Property("Note"),
                    DomainExpression.Literal("x"))
            ],
            OnExitEffects: []);
        var entity = new Entity("Ticket", [note], Actions: [], Policies: [], Stages: [draft, active]);
        var inst = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Note"] = "" });
        await Assert.That(inst.Domain).IsNull();

        var ex = Assert.Throws<InvalidOperationException>(() => inst.TransitionStage("Active"));
        await Assert.That(ex!.Message).Contains("without a Domain-bound module");
    }

    [Test]
    public async Task SliceB_EntryExitSegmentBody_MatchesGetOrLowerIdentity() {
        var (domain, analysis, session) = Evolve("""
            domain Flow
            Ticket: entity {
              Note: Text
              Log: Text
              Draft: stage { }
              Mid: stage {
                entry {
                  assign Note to "before"
                  transition to Active
                  assign Log to "after"
                }
              }
              Active: stage {
                entry { assign Note to "active" }
              }
            }
            """);
        _ = session.Lower(domain, analysis);

        await Assert.That(RuntimeAnalysisCache.TryGetEntryExitSegmentBody(
            domain, "Ticket", "Mid", "entry", 0, out var seg0)).IsTrue();
        await Assert.That(RuntimeAnalysisCache.TryGetEntryExitSegmentBody(
            domain, "Ticket", "Mid", "entry", 0, out var seg0Again)).IsTrue();
        await Assert.That(ReferenceEquals(seg0, seg0Again)).IsTrue();

        // No-ST Active entry: EntryExitBodies populated; segment map skipped (F5).
        await Assert.That(RuntimeAnalysisCache.TryGetEntryExitBody(
            domain, "Ticket", "Active", "entry", out var activeEntry)).IsTrue();
        await Assert.That(activeEntry).IsNotNull();
        await Assert.That(RuntimeAnalysisCache.TryGetEntryExitSegmentBody(
            domain, "Ticket", "Active", "entry", 0, out _)).IsFalse();

        // Mixed Mid: segments are distinct from the whole-batch EntryExitBodies twin.
        await Assert.That(RuntimeAnalysisCache.TryGetEntryExitBody(
            domain, "Ticket", "Mid", "entry", out var midEntry)).IsTrue();
        await Assert.That(ReferenceEquals(seg0, midEntry)).IsFalse();
    }

    private static (Domain Domain, AnalysisResult Analysis, DomainSession Session) Evolve(string poly) {
        var session = DomainSession.ForSource(poly, ExtensionCatalog.ProductAuthoring);
        var changes = new PolyDslParser(poly, session).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes, session: session);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ",
                result.Analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        return (result.Root!, result.Analysis, session);
    }
}
