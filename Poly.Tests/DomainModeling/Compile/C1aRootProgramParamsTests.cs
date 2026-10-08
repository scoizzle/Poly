using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation;
using Poly.Interpretation.CSharp;
using Poly.Interpretation.Vm;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// C1a: root module bodies declare real parameter slots after SetArgs(this).
/// BindForSimulate no longer rewrites action params / previousStage to bag members.
/// </summary>
public class C1aRootProgramParamsTests {
    [Test]
    public async Task C1a_ActionWithParams_ModuleBodyKeepsParameter_AndInvokeBindsPlate() {
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
        var module = session.Lower(domain, analysis);
        var issue = module.First(t => t.Name == "Lot").Methods!.First(m => m.Name == "Issue");
        await Assert.That(ContainsNode<Parameter>(issue.Body!, "plate")).IsTrue();
        await Assert.That(ContainsNode<Member>(issue.Body!, "plate")).IsFalse();
        var printed = new CSharpGenerator().Generate(issue.Body!);
        await Assert.That(printed.Contains("plate", StringComparison.Ordinal)).IsTrue();
        await Assert.That(printed.Contains("this.plate", StringComparison.Ordinal)).IsFalse();

        var lotE = domain.Types.OfType<Entity>().First(e => e.Name == "Lot");
        var store = new DomainInstanceStore();
        var lot = DomainEntityInstance.Create(lotE, domain: domain);
        store.Add(lot);
        var result = lot.InvokeAction("Issue", new Dictionary<string, object?> { ["plate"] = "AAA-1" });
        await Assert.That(result.Succeeded).IsTrue().Because(result.ErrorMessage ?? "");
        await Assert.That(lot.CreatedChildren.Count).IsEqualTo(1);
        await Assert.That(lot.CreatedChildren[0].GetProperty<object>("Plate")).IsEqualTo("AAA-1");
    }

    [Test]
    public async Task C1a_WrongTypedActionArgs_FailLoudLikeBagMemberReads() {
        var (domain, analysis, session) = Evolve("""
            domain T
            Item: entity {
              Qty: Number
              Name: Text
              Flag: Boolean
              SetQty: action (n: Number) { assign Qty to n }
              SetName: action (name: Text) { assign Name to name }
              SetFlag: action (flag: Boolean) { assign Flag to flag }
            }
            """);
        session.Lower(domain, analysis);
        var itemE = domain.Types.OfType<Entity>().First(e => e.Name == "Item");
        var item = DomainEntityInstance.Create(itemE,
            new Dictionary<string, object?> { ["Qty"] = 0L, ["Name"] = "", ["Flag"] = false },
            domain);

        var exNum = Assert.Throws<InvalidOperationException>(() =>
            item.InvokeAction("SetQty", new Dictionary<string, object?> { ["n"] = "not-a-number" }));
        await Assert.That(exNum!.Message).Contains("Cannot store a value of type 'String' in a numeric property");

        var exBool = Assert.Throws<InvalidOperationException>(() =>
            item.InvokeAction("SetQty", new Dictionary<string, object?> { ["n"] = true }));
        await Assert.That(exBool!.Message).Contains("Cannot store a Boolean value in a numeric property");

        var exText = Assert.Throws<InvalidOperationException>(() =>
            item.InvokeAction("SetName", new Dictionary<string, object?> { ["name"] = 7L }));
        await Assert.That(exText!.Message).Contains("Cannot store a value of type 'Int64' in a Text property");

        var exYes = Assert.Throws<InvalidOperationException>(() =>
            item.InvokeAction("SetFlag", new Dictionary<string, object?> { ["flag"] = "yes" }));
        await Assert.That(exYes!.Message).Contains("Cannot store a value of type 'String' in a Boolean property");
    }

    [Test]
    public async Task C1a_WhenAll_PreviousStageParameter_FiresOnce() {
        // Same shape as WhenAllSimulatePrintAgreeTests — previousStage is a real
        // SetArgs slot (not Constant-rewritten / not bag Member). Fires once.
        var (domain, analysis, session) = Evolve("""
            domain Watch
            WorkItem: entity {
              Code: Text
              Draft: stage {
                Prep: action { transition to Ready }
                Finish: action { transition to Done }
              }
              Ready: stage { Finish: action { transition to Done } }
              Done: stage { }
            }
            Board: entity {
              Fires: Number default(0)
              items: many WorkItem
              when all items Ready, Done {
                assign Fires to Fires + 1
              }
            }
            """);
        session.Lower(domain, analysis);
        var itemE = domain.Types.OfType<Entity>().First(e => e.Name == "WorkItem");
        var boardE = domain.Types.OfType<Entity>().First(e => e.Name == "Board");
        var store = new DomainInstanceStore();
        var item1 = DomainEntityInstance.Create(itemE,
            new Dictionary<string, object?> { ["Code"] = "T1" }, domain);
        var item2 = DomainEntityInstance.Create(itemE,
            new Dictionary<string, object?> { ["Code"] = "T2" }, domain);
        var board = DomainEntityInstance.Create(boardE,
            new Dictionary<string, object?> { ["Fires"] = 0L }, domain);
        store.Add(item1); store.Add(item2); store.Add(board);
        store.Link("items", board, item1);
        store.Link("items", board, item2);

        await Assert.That(item1.InvokeAction("Prep").Succeeded).IsTrue();
        await Assert.That(board.GetProperty<object>("Fires")).IsEqualTo(0L);
        await Assert.That(item2.InvokeAction("Finish").Succeeded).IsTrue();
        await Assert.That(board.GetProperty<object>("Fires")).IsEqualTo(1L);
        await Assert.That(item1.InvokeAction("Finish").Succeeded).IsTrue();
        await Assert.That(board.GetProperty<object>("Fires")).IsEqualTo(1L);
    }

    [Test]
    public async Task C1a_RootCompile_ParameterAfterThis_ReadsSetArgsNotInstance() {
        // SetArgs(this, plate): plate must be the string, never the receiver object.
        var plate = new Parameter("plate", TypeReference.To<string>());
        var body = plate;
        var analysis = Interpreter.Analyzer.Analyze(body);
        var program = Interpreter.Compile(body, analysis, [plate]);
        await Assert.That(program.RootParameterClrTypes).IsNotNull();
        await Assert.That(program.RootParameterClrTypes!.Count).IsGreaterThanOrEqualTo(2);

        var receiver = new object();
        using var exec = Interpreter.Execute(program, s => s.SetArgs(receiver, "AAA-1"));
        await Assert.That(exec.GetValue<string>()).IsEqualTo("AAA-1");
    }

    [Test]
    public async Task C1a_RootCompile_LocalsDoNotWipeParameterSlots() {
        // Two locals: without ReserveFrameSlots they occupy slot 0 (this) then slot 1 (plate).
        var plate = new Parameter("plate", TypeReference.To<string>());
        var create0 = new Variable("create0");
        var create1 = new Variable("create1");
        var body = new Block([
            new Assignment(create0, new Constant("local-wipe-0")),
            new Assignment(create1, new Constant("local-wipe-1")),
            new Return(plate)
        ], [create0, create1]);
        var analysis = Interpreter.Analyzer.Analyze(body);
        var program = Interpreter.Compile(body, analysis, [plate]);
        using var exec = Interpreter.Execute(program, s => s.SetArgs(new object(), "KEEP"));
        await Assert.That(exec.GetValue<string>()).IsEqualTo("KEEP");
    }

    private static bool ContainsNode<T>(Node node, string? name = null) where T : Node {
        if (node is T match) {
            if (name is null) return true;
            return match switch {
                Parameter p => string.Equals(p.Name, name, StringComparison.Ordinal),
                Member m => string.Equals(m.MemberName, name, StringComparison.Ordinal),
                Variable v => string.Equals(v.Name, name, StringComparison.Ordinal),
                _ => true
            };
        }
        foreach (var child in node.Children) {
            if (child is not null && ContainsNode<T>(child, name))
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
