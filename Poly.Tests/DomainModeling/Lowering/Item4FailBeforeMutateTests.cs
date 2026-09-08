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
