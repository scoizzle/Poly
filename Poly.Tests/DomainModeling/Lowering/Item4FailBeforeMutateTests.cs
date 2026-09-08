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
/// Item 4: sequential Failure fail-before-mutate in the Lower tree
/// (ProbeCreate prefix with post-prior-assign conditions). Not DEI restore.
/// </summary>
public class Item4FailBeforeMutateTests {
    [Test]
    public async Task LowerActionBody_OccupyIfOnMutatedProperty_ProbeCreateBeforeOpenStaysAssign() {
        var (domain, analysis) = Evolve("""
            domain Hotel
            Stay: entity {
              Nights: Number range(1, 21) required
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
        var probeIdx = flat.FindIndex(n =>
            n is Invoke { Delegate: Member { MemberName: "ProbeCreate" } });
        var assignIdx = flat.FindIndex(n =>
            n is Assignment { Destination: Member { MemberName: "OpenStays" } });
        await Assert.That(probeIdx).IsGreaterThanOrEqualTo(0);
        await Assert.That(assignIdx).IsGreaterThan(probeIdx);
    }

    [Test]
    public async Task ModuleBook_OccupyIfOnMutatedProperty_ProbeCreateBeforeOpenStaysAssign() {
        var (domain, analysis, session) = EvolveWithSession("""
            domain Hotel
            Stay: entity {
              Nights: Number range(1, 21) required
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
        var probeIdx = flat.FindIndex(n =>
            n is Invoke { Delegate: Member { MemberName: "ProbeCreate" } });
        var assignIdx = flat.FindIndex(n =>
            n is Assignment { Destination: Member { MemberName: "OpenStays" } });
        await Assert.That(probeIdx).IsGreaterThanOrEqualTo(0);
        await Assert.That(assignIdx).IsGreaterThan(probeIdx);

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
    public async Task LowerActionBody_NestedIfOnMutatedProperty_InheritsPriorAssignRhs() {
        var (domain, analysis) = Evolve("""
            domain Hotel
            Stay: entity {
              Nights: Number range(1, 21) required
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
        var probeIdx = flat.FindIndex(n =>
            n is Invoke { Delegate: Member { MemberName: "ProbeCreate" } });
        var assignIdx = flat.FindIndex(n =>
            n is Assignment { Destination: Member { MemberName: "OpenStays" } });
        await Assert.That(probeIdx).IsGreaterThanOrEqualTo(0);
        await Assert.That(assignIdx).IsGreaterThan(probeIdx);
        var probePrefix = flat.Take(probeIdx + 1).ToList();
        var hasOpenStaysPlusOne = probePrefix.Any(n =>
            n is global::Poly.Ast.Nodes.Add add
            && add.LeftHandValue is Member { MemberName: "OpenStays" }
            && add.RightHandValue is Constant { Value: 1L or 1 });
        await Assert.That(hasOpenStaysPlusOne).IsTrue();
    }

    [Test]
    public async Task LowerActionBody_QuantifierBody_DoesNotSubstituteRelatedActive() {
        // Entry/exit VM path (UseThisReference false): subject assign Active must
        // not rewrite related-entity Active inside any-body for ProbeCreate guard.
        var (domain, analysis) = Evolve("""
            domain Yard
            Widget: entity {
              Active: Boolean default(false)
              Label: Text required
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
            Domain: domain,
            UseThisReference: false));
        var lowered = pass.LowerActionBody(action.Effects);
        await Assert.That(lowered).IsNotNull();
        var flat = Flatten(lowered!).ToList();
        var anyRelated = flat.OfType<Invoke>().FirstOrDefault(inv =>
            inv.Delegate is Member { MemberName: "AnyRelated" });
        await Assert.That(anyRelated).IsNotNull();
        // StoreQuantifier packs DomainExpression body as Constant — must stay
        // PropertyAccess Active (related), not Literal(true) from subject assign.
        var bodyConst = anyRelated!.Arguments.OfType<Constant>()
            .Select(c => c.Value)
            .OfType<DomainExpression>()
            .FirstOrDefault();
        await Assert.That(bodyConst).IsNotNull();
        await Assert.That(bodyConst).IsTypeOf<PropertyAccess>();
        await Assert.That(((PropertyAccess)bodyConst!).Name).IsEqualTo("Active");
        await Assert.That(bodyConst is Literal).IsFalse();
    }

    private static (Domain Domain, AnalysisResult Analysis) Evolve(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
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
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes, session: session);
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
