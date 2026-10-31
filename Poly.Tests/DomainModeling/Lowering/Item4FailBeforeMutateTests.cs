using Poly.Ast.Nodes;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// Item 4: a failed create leaves earlier assigns unchanged.
/// The printed method is the handwritten order (assign, then create).
/// The interpreter restores the snapshot when the method returns Failure.
/// </summary>
public class Item4FailBeforeMutateTests {
    [Test]
    public async Task LowerActionBody_OccupyIfOnMutatedProperty_AssignThenCreate() {
        var (domain, analysis) = Evolve("""
            domain Hotel
            Stay: entity {
              Nights: Number range(1, 21)
            }
            Guest: entity {
              OpenStays: Number default(0)
              Book: action (nights: Number) {
                assign OpenStays to OpenStays + 1
                if (OpenStays >= 1) {
                  create Stay { Nights: nights }
                }
              }
            }
            """);
        var guest = domain.Types.OfType<Entity>().First(e => e.Name == "Guest");
        var action = guest.Actions.First(a => a.Name == "Book");
        var pass = new EffectLoweringPass(guest, new LoweringContext(
            new Parameter("entity", new TypeReference(guest.Name)),
            Analysis: analysis,
            Domain: domain,
            ActionParameterNames: ["nights"]));
        var lowered = pass.LowerActionBody(action.Effects);
        await Assert.That(lowered).IsNotNull();
        var flat = Flatten(lowered!).ToList();
        var createIdx = flat.FindIndex(n =>
            n is Invoke { Delegate: Member { MemberName: "Create" } });
        var assignIdx = flat.FindIndex(n =>
            n is Assignment { Destination: Member { MemberName: "OpenStays" } });
        await Assert.That(flat.Any(n =>
            n is Invoke { Delegate: Member { MemberName: "ProbeCreate" } })).IsFalse();
        await Assert.That(assignIdx).IsGreaterThanOrEqualTo(0);
        await Assert.That(createIdx).IsGreaterThan(assignIdx);
    }

    [Test]
    public async Task ModuleBook_OccupyIfOnMutatedProperty_AssignThenCreate_FailureRestores() {
        var (domain, analysis, session) = EvolveWithSession("""
            domain Hotel
            Stay: entity {
              Nights: Number range(1, 21)
            }
            Guest: entity {
              OpenStays: Number default(0)
              Book: action (nights: Number) {
                assign OpenStays to OpenStays + 1
                if (OpenStays >= 1) {
                  create Stay { Nights: nights }
                }
              }
            }
            """);
        session.Lower(domain, analysis);
        await Assert.That(RuntimeAnalysisCache.TryGetModuleMethod(
            domain, "Guest", "Book", out var book)).IsTrue();
        await Assert.That(book?.Body).IsNotNull();
        var flat = Flatten(book!.Body!).ToList();
        var createIdx = flat.FindIndex(n =>
            n is Invoke { Delegate: Member { MemberName: "Create" } });
        var assignIdx = flat.FindIndex(n =>
            n is Assignment { Destination: Member { MemberName: "OpenStays" } });
        await Assert.That(flat.Any(n =>
            n is Invoke { Delegate: Member { MemberName: "ProbeCreate" } })).IsFalse();
        await Assert.That(assignIdx).IsGreaterThanOrEqualTo(0);
        await Assert.That(createIdx).IsGreaterThan(assignIdx);

        // Runtime fail-before-mutate (not order-only): Nights Failure, bag unchanged.
        var guestEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Guest");
        var guest = DomainEntityInstance.Create(guestEntity, domain: domain);
        var store = new DomainInstanceStore();
        store.Add(guest);
        var result = guest.InvokeAction("Book",
            new Dictionary<string, object?> { ["nights"] = 0L });
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.ErrorMessage).Contains("Nights");
        await Assert.That(guest.GetProperty<object>("OpenStays")).IsEqualTo(0L);
        await Assert.That(guest.CreatedChildren).IsEmpty();
    }

    [Test]
    public async Task StoreLess_OccupyIfOnMutatedProperty_FailureLeavesOpenStaysUnchanged() {
        var (domain, _) = Evolve("""
            domain Hotel
            Stay: entity {
              Nights: Number range(1, 21)
            }
            Guest: entity {
              OpenStays: Number default(0)
              Book: action (nights: Number) {
                assign OpenStays to OpenStays + 1
                if (OpenStays >= 1) {
                  create Stay { Nights: nights }
                }
              }
            }
            """);
        var guestEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Guest");
        var guest = DomainEntityInstance.Create(guestEntity, domain: domain);
        await Assert.That(guest.Store).IsNull();
        var result = guest.InvokeAction("Book",
            new Dictionary<string, object?> { ["nights"] = 0L });
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.ErrorMessage).Contains("Nights");
        await Assert.That(guest.GetProperty<object>("OpenStays")).IsEqualTo(0L);
        await Assert.That(guest.CreatedChildren).IsEmpty();
    }

    [Test]
    public async Task LowerActionBody_NestedIfOnMutatedProperty_InheritsPriorAssignRhs() {
        var (domain, analysis) = Evolve("""
            domain Hotel
            Stay: entity {
              Nights: Number range(1, 21)
            }
            Guest: entity {
              OpenStays: Number default(0)
              Book: action (nights: Number, confirm: Boolean) {
                assign OpenStays to OpenStays + 1
                if (confirm) {
                  if (OpenStays >= 1) {
                    create Stay { Nights: nights }
                  }
                }
              }
            }
            """);
        var guest = domain.Types.OfType<Entity>().First(e => e.Name == "Guest");
        var action = guest.Actions.First(a => a.Name == "Book");
        var pass = new EffectLoweringPass(guest, new LoweringContext(
            new Parameter("entity", new TypeReference(guest.Name)),
            Analysis: analysis,
            Domain: domain,
            ActionParameterNames: ["nights", "confirm"]));
        var lowered = pass.LowerActionBody(action.Effects);
        await Assert.That(lowered).IsNotNull();
        var flat = Flatten(lowered!).ToList();
        var createIdx = flat.FindIndex(n =>
            n is Invoke { Delegate: Member { MemberName: "Create" } });
        var assignIdx = flat.FindIndex(n =>
            n is Assignment { Destination: Member { MemberName: "OpenStays" } });
        await Assert.That(flat.Any(n =>
            n is Invoke { Delegate: Member { MemberName: "ProbeCreate" } })).IsFalse();
        await Assert.That(assignIdx).IsGreaterThanOrEqualTo(0);
        await Assert.That(createIdx).IsGreaterThan(assignIdx);
    }

    [Test]
    public async Task LowerActionBody_QuantifierBody_DoesNotSubstituteRelatedActive() {
        // Subject assign Active must not rewrite related-entity Active inside
        // the any-body.
        var (domain, analysis) = Evolve("""
            domain Yard
            Widget: entity {
              Active: Boolean default(false)
              Label: Text
            }
            Bin: entity {
              Active: Boolean default(false)
              widgets: many Widget
              Seed: action {
                assign Active to true
                if (any widgets where Active) {
                  create Widget { Label: "x" }
                }
              }
            }
            """);
        var bin = domain.Types.OfType<Entity>().First(e => e.Name == "Bin");
        var action = bin.Actions.First(a => a.Name == "Seed");
        var pass = new EffectLoweringPass(bin, new LoweringContext(
            new Parameter("entity", new TypeReference(bin.Name)),
            Analysis: analysis,
            Domain: domain));
        var lowered = pass.LowerActionBody(action.Effects);
        await Assert.That(lowered).IsNotNull();
        var flat = Flatten(lowered!).ToList();
        var loop = flat.OfType<ForEachLoop>().FirstOrDefault(l =>
            l.Collection is Member { MemberName: "Widgets" });
        await Assert.That(loop).IsNotNull();
        // Related-entity Active is Member on the loop variable, not Literal(true)
        // from the subject assign Active to true.
        var activeOnItem = Flatten(loop!.Body).OfType<Member>().FirstOrDefault(m =>
            m.MemberName == "Active" && ReferenceEquals(m.Value, loop.LoopVariable));
        await Assert.That(activeOnItem).IsNotNull();
        await Assert.That(LoopIsSiblingBeforeIf(lowered!, loop!)).IsTrue();
    }

    private static bool LoopIsSiblingBeforeIf(Node node, ForEachLoop loop) {
        if (node is Block block) {
            var loopIdx = -1;
            for (var i = 0; i < block.Nodes.Count; i++) {
                if (ReferenceEquals(block.Nodes[i], loop))
                    loopIdx = i;
                if (loopIdx >= 0 && i > loopIdx && block.Nodes[i] is IfStatement ifs
                    && !Flatten(ifs.Condition).OfType<ForEachLoop>().Any())
                    return true;
            }
            foreach (var child in block.Nodes) {
                if (LoopIsSiblingBeforeIf(child, loop))
                    return true;
            }
        }
        return false;
    }

    private static (Domain Domain, AnalysisResult Analysis) Evolve(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ",
                result.Analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        var analysis = DomainModelAnalyzer.Analyze(result.Root!);
        return (result.Root!, analysis);
    }

    private static (Domain Domain, AnalysisResult Analysis, DomainSession Session) EvolveWithSession(
        string poly) {
        var session = DomainSession.ForSource(poly, ExtensionCatalog.ProductAuthoring);
        var changes = new PolyDslParser(poly, session).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes, session: session);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ",
                result.Analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        return (result.Root!, result.Analysis, session);
    }

    private static IEnumerable<Node> Flatten(Node node) {
        yield return node;
        foreach (var child in node.Children) {
            if (child is null)
                continue;
            foreach (var n in Flatten(child))
                yield return n;
        }
    }
}