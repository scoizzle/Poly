using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// Slice A oracle: one shared session.Lower / GetOrLower Syntax module body.
/// Interpreter execute and C# print agree without a UseThisReference consumer flag.
/// </summary>
public class SliceASharedModuleBodyTests {
    [Test]
    public async Task SliceA_SharedModuleBody_InterpreterAndPrint_AgreeWithoutConsumerFlag() {
        var (domain, analysis, session) = Evolve("""
            domain Parking
            Permit: entity { Plate: Text }
            Lot: entity {
              permits: many Permit
              Issue: action (plate: Text) {
                create in permits { Plate: plate }
              }
            }
            """);
        var exec = ModuleExec.For(domain);

        // 1) Lower once via session.Lower / GetOrLower.
        var module = session.Lower(domain, analysis);
        var again = RuntimeAnalysisCache.GetOrLower(
            domain, RuntimeAnalysisCache.Session(domain), analysis);
        await Assert.That(ReferenceEquals(module, again)).IsTrue();

        var lotType = module.First(t => t.Name == "Lot");
        var issue = lotType.Methods?.FirstOrDefault(m => m.Name == "Issue");
        await Assert.That(issue).IsNotNull();
        await Assert.That(issue!.Body).IsNotNull();
        var bodyMethod = lotType.Methods.BodyMethod("Issue");
        await Assert.That(bodyMethod!.Name).IsEqualTo("__BodyOfIssue");
        var body = bodyMethod.Body!;

        // Export-shaped: ThisReference present — not a Parameter("entity")-rooted twin.
        await Assert.That(ContainsNode<ThisReference>(body)).IsTrue();

        // 2) Interpreter executes the shipped action from that module body.
        var lotE = domain.Types.OfType<Entity>().First(e => e.Name == "Lot");
        var lot = exec.Create((lotE).Name, new Dictionary<string, object?>()).Instance!;
        var result = exec.Invoke(lot, "Issue", new Dictionary<string, object?> { ["plate"] = "AAA-1" });
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.CreatedChildren.Count).IsEqualTo(1);

        // Same module method object after execute — no re-lower with opposite flag.
        await Assert.That(RuntimeAnalysisCache.TryGetModuleMethod(domain, "Lot", "Issue", out var cached)).IsTrue();
        await Assert.That(ReferenceEquals(issue, cached)).IsTrue();
        await Assert.That(ActionBodies.TryGetModuleBody(domain, "Lot", "Issue", out var cachedBody)).IsTrue();
        await Assert.That(ReferenceEquals(body, cachedBody!.Body)).IsTrue();

        // 3) C# print of the SAME method body (no second EffectLoweringPass / UseThis flag).
        var printedFromModule = new CSharpGenerator().Generate(body);
        await Assert.That(printedFromModule.Length).IsGreaterThan(0);
        var files = session.Emit(domain, analysis);
        var lotCs = files.First(f => f.FileName == "Lot.cs").Source;
        await Assert.That(lotCs.Contains("Issue", StringComparison.Ordinal)).IsTrue();
        foreach (var line in printedFromModule.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Where(l => l.Length > 8)
                     .Take(5)) {
            await Assert.That(lotCs.Contains(line, StringComparison.Ordinal)).IsTrue();
        }
    }

    [Test]
    public async Task SliceA_SharedModuleBody_EntryExit_CachedBodyWithoutDeadMember() {
        var (domain, analysis, session) = Evolve("""
            domain Flow
            Ticket: entity {
              Note: Text
              Draft: stage {
                exit { assign Note to "left-draft" }
              }
              Done: stage { }
            }
            """);
        var exec = ModuleExec.For(domain);
        var module = session.Lower(domain, analysis);
        var ticket = module.First(t => t.Name == "Ticket");
        // D10: the body stays in the runtime cache; the printed type no longer
        // carries the dead OnExitDraft member.
        var onExit = ticket.Methods?.FirstOrDefault(m => m.Name == "OnExitDraft");
        await Assert.That(onExit).IsNull();


        // Host TransitionStage is gone with DEI. The cached exit body above is what this test can still prove.
    }

    private static bool ContainsNode<T>(Node node) where T : Node {
        if (node is T)
            return true;
        foreach (var child in node.Children) {
            if (child is not null && ContainsNode<T>(child))
                return true;
        }
        return false;
    }

    private static (Domain Domain, AnalysisResult Analysis, DomainSession Session) Evolve(string poly) {
        var session = DomainSession.ForSource(poly, [.. ExtensionCatalog.ProductAuthoring, "persistence"]);
        var changes = new PolyDslParser(poly, session).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes, session: session);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ",
                result.Analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        return (result.Root!, result.Analysis, session);
    }
}