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
            Permit: entity { Plate: Text required }
            Lot: entity {
              permits: many Permit
              Issue: action (plate: Text) {
                create in permits { Plate: plate }
              }
            }
            """);

        // 1) Lower once via session.Lower / GetOrLower.
        var module = session.Lower(domain, analysis);
        var again = RuntimeAnalysisCache.GetOrLower(
            domain, RuntimeAnalysisCache.Session(domain), analysis);
        await Assert.That(ReferenceEquals(module, again)).IsTrue();

        var lotType = module.First(t => t.Name == "Lot");
        var issue = lotType.Methods?.FirstOrDefault(m => m.Name == "Issue");
        await Assert.That(issue).IsNotNull();
        await Assert.That(issue!.Body).IsNotNull();
        var body = issue.Body!;

        // Export-shaped: ThisReference present — not a Parameter("entity")-rooted twin.
        await Assert.That(ContainsNode<ThisReference>(body)).IsTrue();

        // 2) Interpreter executes the shipped action from that module body.
        var lotE = domain.Types.OfType<Entity>().First(e => e.Name == "Lot");
        var store = new DomainInstanceStore();
        var lot = DomainEntityInstance.Create(lotE, domain: domain);
        store.Add(lot);
        var result = lot.InvokeAction("Issue", new Dictionary<string, object?> { ["plate"] = "AAA-1" });
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(lot.CreatedChildren.Count).IsEqualTo(1);

        // Same module method object after execute — no re-lower with opposite flag.
        await Assert.That(RuntimeAnalysisCache.TryGetModuleMethod(domain, "Lot", "Issue", out var cached)).IsTrue();
        await Assert.That(ReferenceEquals(issue, cached)).IsTrue();
        await Assert.That(ReferenceEquals(body, cached!.Body)).IsTrue();

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
    public async Task SliceA_SharedModuleBody_EntryExit_SameTreeAsModuleMethod() {
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
        var module = session.Lower(domain, analysis);
        var ticket = module.First(t => t.Name == "Ticket");
        var onExit = ticket.Methods?.FirstOrDefault(m => m.Name == "OnExitDraft");
        await Assert.That(onExit).IsNotNull();
        await Assert.That(onExit!.Body).IsNotNull();
        await Assert.That(ContainsNode<ThisReference>(onExit.Body!)).IsTrue();

        await Assert.That(RuntimeAnalysisCache.TryGetEntryExitBody(
            domain, "Ticket", "Draft", "exit", out var exitBody)).IsTrue();
        await Assert.That(exitBody).IsNotNull();
        await Assert.That(ReferenceEquals(onExit.Body, exitBody)).IsTrue();

        var printed = new CSharpGenerator().Generate(onExit.Body!);
        await Assert.That(printed.Length).IsGreaterThan(0);

        var ticketE = domain.Types.OfType<Entity>().First(e => e.Name == "Ticket");
        var inst = DomainEntityInstance.Create(ticketE,
            new Dictionary<string, object?> { ["Note"] = "" }, domain);
        await Assert.That(inst.CurrentStage).IsEqualTo("Draft");
        inst.TransitionStage("Done");
        await Assert.That(inst.CurrentStage).IsEqualTo("Done");
        await Assert.That(inst.GetProperty<string>("Note")).IsEqualTo("left-draft");
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
