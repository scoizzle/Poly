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
/// One .poly model with <c>when all</c>: simulate runs the module handler
/// session.Lower produced, and printed C# is that same body.
/// </summary>
public class WhenAllSimulatePrintAgreeTests {
    [Test]
    public async Task WhenAll_MultiStage_SimulateAndPrintedCsharp_SameFireCounts() {
        var poly = """
            domain Watch
            WorkItem: entity {
              Code: Text
              Draft: stage {
                Prep: action { transition to Ready }
                Finish: action { transition to Done }
              }
              Ready: stage {
                Finish: action { transition to Done }
              }
              Done: stage { }
            }
            Board: entity {
              Fires: Number default(0)
              items: many WorkItem
              when all items Ready, Done {
                assign Fires to Fires + 1
              }
            }
            """;
        var (domain, analysis, session) = Evolve(poly);
        var module = session.Lower(domain, analysis);
        var boardType = module.First(t => t.Name == "Board");
        var handler = boardType.Methods?.FirstOrDefault(m => m.Name == "WhenAllWorkItemReady");
        await Assert.That(handler).IsNotNull();
        await Assert.That(handler!.Body).IsNotNull();
        await Assert.That(ContainsNode<ThisReference>(handler.Body!)).IsTrue();
        await Assert.That(ContainsNode<Parameter>(handler.Body!)).IsTrue();

        var printed = new CSharpGenerator().Generate(handler.Body!);
        await Assert.That(printed.Contains("linkedMatched", StringComparison.Ordinal)).IsTrue();
        await Assert.That(printed.Contains("previousStage", StringComparison.Ordinal)).IsTrue();
        var files = session.Emit(domain, analysis);
        var boardCs = files.First(f => f.FileName == "Board.cs").Source;
        await Assert.That(boardCs.Contains("WhenAllWorkItemReady", StringComparison.Ordinal)).IsTrue();
        await Assert.That(boardCs.Contains("previousStage", StringComparison.Ordinal)).IsTrue();

        var itemE = domain.Types.OfType<Entity>().First(e => e.Name == "WorkItem");
        var boardE = domain.Types.OfType<Entity>().First(e => e.Name == "Board");
        var store = new DomainInstanceStore();
        var item1 = DomainEntityInstance.Create(itemE,
            new Dictionary<string, object?> { ["Code"] = "T1" }, domain);
        var item2 = DomainEntityInstance.Create(itemE,
            new Dictionary<string, object?> { ["Code"] = "T2" }, domain);
        var board = DomainEntityInstance.Create(boardE,
            new Dictionary<string, object?> { ["Fires"] = 0L }, domain);
        store.Add(item1);
        store.Add(item2);
        store.Add(board);
        store.Link("items", board, item1);
        store.Link("items", board, item2);

        await Assert.That(item1.InvokeAction("Prep").Succeeded).IsTrue();
        await Assert.That(board.GetProperty<object>("Fires")).IsEqualTo(0L);
        await Assert.That(item2.InvokeAction("Finish").Succeeded).IsTrue();
        await Assert.That(board.GetProperty<object>("Fires")).IsEqualTo(1L);
        await Assert.That(item1.InvokeAction("Finish").Succeeded).IsTrue();
        await Assert.That(board.GetProperty<object>("Fires")).IsEqualTo(1L);

        var printedFires = RunPrintedAllScenario(session, domain, analysis);
        await Assert.That(printedFires[0]).IsEqualTo(0L);
        await Assert.That(printedFires[1]).IsEqualTo(1L);
        await Assert.That(printedFires[2]).IsEqualTo(1L);
    }

    [Test]
    public async Task WhenAll_OnEntryChain_SimulateAndPrintedCsharp_SameFireCounts() {
        var poly = """
            domain Watch
            WorkItem: entity {
              Code: Text
              Draft: stage {
                Prep: action { transition to Ready }
              }
              Ready: stage {
                entry { transition to Done }
              }
              Done: stage { }
            }
            Board: entity {
              Fires: Number default(0)
              items: many WorkItem
              when all items Ready, Done {
                assign Fires to Fires + 1
              }
            }
            """;
        var (domain, analysis, session) = Evolve(poly);
        var files = session.Emit(domain, analysis);
        var itemCs = files.First(f => f.FileName == "WorkItem.cs").Source;
        await Assert.That(itemCs.Contains("previousStage0", StringComparison.Ordinal)).IsTrue();
        await Assert.That(itemCs.Contains("previousStage1", StringComparison.Ordinal)).IsTrue();
        await Assert.That(itemCs.Contains("var previousStage =", StringComparison.Ordinal)).IsFalse();

        var itemE = domain.Types.OfType<Entity>().First(e => e.Name == "WorkItem");
        var boardE = domain.Types.OfType<Entity>().First(e => e.Name == "Board");
        var store = new DomainInstanceStore();
        var item1 = DomainEntityInstance.Create(itemE,
            new Dictionary<string, object?> { ["Code"] = "T1" }, domain);
        var item2 = DomainEntityInstance.Create(itemE,
            new Dictionary<string, object?> { ["Code"] = "T2" }, domain);
        var board = DomainEntityInstance.Create(boardE,
            new Dictionary<string, object?> { ["Fires"] = 0L }, domain);
        store.Add(item1);
        store.Add(item2);
        store.Add(board);
        store.Link("items", board, item1);
        store.Link("items", board, item2);

        await Assert.That(item1.InvokeAction("Prep").Succeeded).IsTrue();
        await Assert.That(board.GetProperty<object>("Fires")).IsEqualTo(0L);
        await Assert.That(item2.InvokeAction("Prep").Succeeded).IsTrue();
        await Assert.That(board.GetProperty<object>("Fires")).IsEqualTo(1L);

        var printedFires = RunPrintedOnEntryChain(session, domain, analysis);
        await Assert.That(printedFires[0]).IsEqualTo(0L);
        await Assert.That(printedFires[1]).IsEqualTo(1L);
    }

    [Test]
    public async Task WhenAll_StageAndEntityAction_SimulateAndPrintedCsharp_SameFireCounts() {
        var poly = """
            domain Watch
            WorkItem: entity {
              Code: Text
              Finish: action { transition to Done }
              Draft: stage {
                Finish: action { transition to Done }
              }
              Ready: stage { }
              Done: stage { }
            }
            Board: entity {
              Fires: Number default(0)
              items: many WorkItem
              when all items Done {
                assign Fires to Fires + 1
              }
            }
            """;
        var (domain, analysis, session) = Evolve(poly);
        var files = session.Emit(domain, analysis);
        var itemCs = files.First(f => f.FileName == "WorkItem.cs").Source;
        await Assert.That(itemCs.Contains("previousStage0", StringComparison.Ordinal)).IsTrue();
        await Assert.That(itemCs.Contains("previousStage1", StringComparison.Ordinal)).IsTrue();
        await Assert.That(itemCs.Contains("var previousStage =", StringComparison.Ordinal)).IsFalse();

        var itemE = domain.Types.OfType<Entity>().First(e => e.Name == "WorkItem");
        var boardE = domain.Types.OfType<Entity>().First(e => e.Name == "Board");
        var store = new DomainInstanceStore();
        var item1 = DomainEntityInstance.Create(itemE,
            new Dictionary<string, object?> { ["Code"] = "T1" }, domain);
        var item2 = DomainEntityInstance.Create(itemE,
            new Dictionary<string, object?> { ["Code"] = "T2" }, domain);
        var board = DomainEntityInstance.Create(boardE,
            new Dictionary<string, object?> { ["Fires"] = 0L }, domain);
        store.Add(item1);
        store.Add(item2);
        store.Add(board);
        store.Link("items", board, item1);
        store.Link("items", board, item2);

        await Assert.That(item1.InvokeAction("Finish").Succeeded).IsTrue();
        await Assert.That(board.GetProperty<object>("Fires")).IsEqualTo(0L);
        await Assert.That(item2.InvokeAction("Finish").Succeeded).IsTrue();
        await Assert.That(board.GetProperty<object>("Fires")).IsEqualTo(1L);

        var printedFires = RunPrintedStageAndEntityAction(session, domain, analysis);
        await Assert.That(printedFires[0]).IsEqualTo(0L);
        await Assert.That(printedFires[1]).IsEqualTo(1L);
    }

    private static long[] RunPrintedStageAndEntityAction(
        DomainSession session, Domain domain, AnalysisResult analysis) {
        var types = session.Lower(domain, analysis);
        var cs = new CSharpGenerator().Generate(types);
        var asm = WhenAnySimulatePrintAgreeTests.CompileGenerated(cs, "WhenAllStageEntityPrintAgree");
        var board = WhenAnySimulatePrintAgreeTests.CreateEntity(asm, "Board");
        var item1 = WhenAnySimulatePrintAgreeTests.CreateEntity(asm, "WorkItem", ("code", "T1"));
        var item2 = WhenAnySimulatePrintAgreeTests.CreateEntity(asm, "WorkItem", ("code", "T2"));
        WhenAnySimulatePrintAgreeTests.Attach(board, "Items", item1);
        WhenAnySimulatePrintAgreeTests.Attach(board, "Items", item2);

        var fires = new long[2];
        WhenAnySimulatePrintAgreeTests.InvokeAction(item1, "Finish");
        fires[0] = WhenAnySimulatePrintAgreeTests.GetLong(board, "Fires");
        WhenAnySimulatePrintAgreeTests.InvokeAction(item2, "Finish");
        fires[1] = WhenAnySimulatePrintAgreeTests.GetLong(board, "Fires");
        return fires;
    }

    private static long[] RunPrintedOnEntryChain(
        DomainSession session, Domain domain, AnalysisResult analysis) {
        var types = session.Lower(domain, analysis);
        var cs = new CSharpGenerator().Generate(types);
        var asm = WhenAnySimulatePrintAgreeTests.CompileGenerated(cs, "WhenAllOnEntryPrintAgree");
        var board = WhenAnySimulatePrintAgreeTests.CreateEntity(asm, "Board");
        var item1 = WhenAnySimulatePrintAgreeTests.CreateEntity(asm, "WorkItem", ("code", "T1"));
        var item2 = WhenAnySimulatePrintAgreeTests.CreateEntity(asm, "WorkItem", ("code", "T2"));
        WhenAnySimulatePrintAgreeTests.Attach(board, "Items", item1);
        WhenAnySimulatePrintAgreeTests.Attach(board, "Items", item2);

        var fires = new long[2];
        WhenAnySimulatePrintAgreeTests.InvokeAction(item1, "Prep");
        fires[0] = WhenAnySimulatePrintAgreeTests.GetLong(board, "Fires");
        WhenAnySimulatePrintAgreeTests.InvokeAction(item2, "Prep");
        fires[1] = WhenAnySimulatePrintAgreeTests.GetLong(board, "Fires");
        return fires;
    }

    private static long[] RunPrintedAllScenario(
        DomainSession session, Domain domain, AnalysisResult analysis) {
        var types = session.Lower(domain, analysis);
        var cs = new CSharpGenerator().Generate(types);
        var asm = WhenAnySimulatePrintAgreeTests.CompileGenerated(cs, "WhenAllPrintAgree");
        var board = WhenAnySimulatePrintAgreeTests.CreateEntity(asm, "Board");
        var item1 = WhenAnySimulatePrintAgreeTests.CreateEntity(asm, "WorkItem", ("code", "T1"));
        var item2 = WhenAnySimulatePrintAgreeTests.CreateEntity(asm, "WorkItem", ("code", "T2"));
        WhenAnySimulatePrintAgreeTests.Attach(board, "Items", item1);
        WhenAnySimulatePrintAgreeTests.Attach(board, "Items", item2);

        var fires = new long[3];
        WhenAnySimulatePrintAgreeTests.InvokeAction(item1, "Prep");
        fires[0] = WhenAnySimulatePrintAgreeTests.GetLong(board, "Fires");
        WhenAnySimulatePrintAgreeTests.InvokeAction(item2, "Finish");
        fires[1] = WhenAnySimulatePrintAgreeTests.GetLong(board, "Fires");
        WhenAnySimulatePrintAgreeTests.InvokeAction(item1, "Finish");
        fires[2] = WhenAnySimulatePrintAgreeTests.GetLong(board, "Fires");
        return fires;
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
