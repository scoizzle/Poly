using Poly.Ast.Nodes;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.DomainModeling.Lowering;

public class QuantifierLoopTests {
    private static (Domain Domain, AnalysisResult Analysis) Evolve(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ",
                result.Analysis.Diagnostics
                    .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        var analysis = DomainModelAnalyzer.Analyze(result.Root!);
        if (analysis.HasErrors)
            throw new InvalidOperationException(string.Join("; ",
                analysis.Diagnostics
                    .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        return (result.Root!, analysis);
    }

    private const string BasketDsl = """
        domain Basket
        Line: entity {
          Qty: Number
          Open: Boolean
          parts: many Part
        }
        Order: entity {
          lines: many Line
          AnyOpen: policy { any lines where Open }
          AllOpen: policy { all lines where Open }
          NoneOpen: policy { none lines where Open }
          OpenCount: policy { count lines where Open is true == 2 }
          Ready: Boolean
          ReadyWithOpenLine: policy { Ready and (any lines where Open) }
          AnyOpenLineWithBigPart: policy { any lines where Open and (any parts where Qty > 5) }
        }
        Part: entity {
          Qty: Number
        }
        """;

    [Test]
    public async Task Simulate_All_EmptyCollection_IsFalse() {
        var (domain, _) = Evolve(BasketDsl);
        var order = domain.Types.OfType<Entity>().First(e => e.Name == "Order");
        var store = new DomainInstanceStore();
        var inst = DomainEntityInstance.Create(order, domain: domain);
        store.Add(inst);
        await Assert.That(inst.EvaluatePolicy(order.Policies.First(p => p.Name == "AllOpen"))).IsFalse();
        await Assert.That(inst.EvaluatePolicy(order.Policies.First(p => p.Name == "AnyOpen"))).IsFalse();
        await Assert.That(inst.EvaluatePolicy(order.Policies.First(p => p.Name == "NoneOpen"))).IsTrue();
    }

    [Test]
    public async Task Simulate_None_And_CountWhere_MatchLinkedLines() {
        var (domain, _) = Evolve(BasketDsl);
        var orderEnt = domain.Types.OfType<Entity>().First(e => e.Name == "Order");
        var lineEnt = domain.Types.OfType<Entity>().First(e => e.Name == "Line");
        var store = new DomainInstanceStore();
        var order = DomainEntityInstance.Create(orderEnt, domain: domain);
        var openA = DomainEntityInstance.Create(lineEnt,
            new Dictionary<string, object?> { ["Qty"] = 1L, ["Open"] = true }, domain);
        var openB = DomainEntityInstance.Create(lineEnt,
            new Dictionary<string, object?> { ["Qty"] = 2L, ["Open"] = true }, domain);
        var closed = DomainEntityInstance.Create(lineEnt,
            new Dictionary<string, object?> { ["Qty"] = 3L, ["Open"] = false }, domain);
        store.Add(order); store.Add(openA); store.Add(openB); store.Add(closed);
        store.Link("lines", order, openA);
        store.Link("lines", order, openB);
        store.Link("lines", order, closed);

        await Assert.That(order.EvaluatePolicy(orderEnt.Policies.First(p => p.Name == "AnyOpen"))).IsTrue();
        await Assert.That(order.EvaluatePolicy(orderEnt.Policies.First(p => p.Name == "AllOpen"))).IsFalse();
        await Assert.That(order.EvaluatePolicy(orderEnt.Policies.First(p => p.Name == "NoneOpen"))).IsFalse();
        await Assert.That(order.EvaluatePolicy(orderEnt.Policies.First(p => p.Name == "OpenCount"))).IsTrue();
    }

    [Test]
    public async Task Export_QuantifierBody_ComparesTargetEnumNotSourceEnum() {
        var (domain, analysis) = Evolve("""
            domain Shop
            PatronStatus: enum { Active, Closed }
            LoanStatus: enum { Current, Overdue }
            Loan: entity {
              Status: LoanStatus
            }
            Patron: entity {
              Status: PatronStatus
              loans: many Loan
              HasOverdue: policy { any loans where Status is "Overdue" }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(types);
        var method = ExportedCSharp.ExtractMethod(cs, "bool HasOverdue(");
        await Assert.That(method).Contains("LoanStatus.Overdue");
        await Assert.That(method).DoesNotContain("PatronStatus.Overdue");
        await Assert.That(method).Contains("foreach");
    }

    [Test]
    public async Task AnyExpr_LowersToForeachOverNavigation() {
        var (domain, analysis) = Evolve(BasketDsl);
        var order = domain.Types.OfType<Entity>().First(e => e.Name == "Order");
        var policy = order.Policies.First(p => p.Name == "AnyOpen");
        var body = DomainToCSharpExporter.LowerExpressionToMethodBody(
            policy.Expression, order, domain, analysis);
        await Assert.That(body).IsNotNull();
        var loop = Flatten(body!).OfType<ForEachLoop>().FirstOrDefault();
        await Assert.That(loop).IsNotNull();
        await Assert.That(loop!.Collection).IsTypeOf<Member>();
        await Assert.That(((Member)loop.Collection).MemberName).IsEqualTo("Lines");
    }

    [Test]
    public async Task Quantifiers_GiveSameAnswers_SimulatedAndInExportedCSharp() {
        var (domain, analysis) = Evolve(BasketDsl);
        Entity E(string name) => domain.Types.OfType<Entity>().First(e => e.Name == name);
        var policyNames = E("Order").Policies.Select(p => p.Name).ToArray();

        // Simulated: instances linked through a store.
        var store = new DomainInstanceStore();
        DomainEntityInstance SimPart(long qty) {
            var part = DomainEntityInstance.Create(E("Part"), new Dictionary<string, object?> { ["Qty"] = qty }, domain);
            store.Add(part);
            return part;
        }
        DomainEntityInstance SimLine(bool open, params DomainEntityInstance[] parts) {
            var line = DomainEntityInstance.Create(E("Line"),
                new Dictionary<string, object?> { ["Qty"] = 1L, ["Open"] = open }, domain);
            store.Add(line);
            foreach (var part in parts) store.Link("parts", line, part);
            return line;
        }
        DomainEntityInstance SimOrder(bool ready, params DomainEntityInstance[] lines) {
            var order = DomainEntityInstance.Create(E("Order"),
                new Dictionary<string, object?> { ["Ready"] = ready }, domain);
            store.Add(order);
            foreach (var line in lines) store.Link("lines", order, line);
            return order;
        }

        // Printed: the same shapes built from the exported, compiled types.
        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis)));
        object Make(string type, params (string Name, object? Value)[] args) =>
            ExportedCSharp.CreateEntity(asm, type, args);
        Array ListOf(string type, object[] items) {
            var array = Array.CreateInstance(asm.GetType(type)!, items.Length);
            items.CopyTo(array, 0);
            return array;
        }
        object PrintPart(long qty) => Make("Part", ("qty", qty));
        object PrintLine(bool open, params object[] parts) =>
            Make("Line", ("qty", 1L), ("open", open), ("parts", ListOf("Part", parts)));
        object PrintOrder(bool ready, params object[] lines) =>
            Make("Order", ("ready", ready), ("lines", ListOf("Line", lines)));

        var cases = new (string Name, DomainEntityInstance Simulated, object Printed)[] {
            ("no lines", SimOrder(true), PrintOrder(true)),
            ("mixed lines", SimOrder(true, SimLine(true, SimPart(9)), SimLine(true), SimLine(false)),
                PrintOrder(true, PrintLine(true, PrintPart(9)), PrintLine(true), PrintLine(false))),
            ("all open, small parts, not ready", SimOrder(false, SimLine(true, SimPart(1)), SimLine(true)),
                PrintOrder(false, PrintLine(true, PrintPart(1)), PrintLine(true))),
        };
        var bigPart = E("Order").Policies.First(p => p.Name == "AnyOpenLineWithBigPart");
        await Assert.That(cases[1].Simulated.EvaluatePolicy(bigPart)).IsTrue();
        await Assert.That(cases[2].Simulated.EvaluatePolicy(bigPart)).IsFalse();

        foreach (var (name, simulated, printed) in cases) {
            foreach (var policyName in policyNames) {
                var sim = simulated.EvaluatePolicy(E("Order").Policies.First(p => p.Name == policyName));
                var print = (bool)printed.GetType().GetMethod(policyName)!.Invoke(printed, null)!;
                await Assert.That(print).IsEqualTo(sim).Because($"{policyName} on {name}");
            }
        }
    }

    private const string BinDsl = """
        domain Yard
        Widget: entity { Active: Boolean default(false) }
        Bin: entity {
          Active: Boolean default(false)
          N: Number default(0)
          widgets: many Widget
          Mark: action { if (any widgets where Active) { assign Active to true } }
          Tally: action { assign N to count widgets where Active }
          Guarded: action { if (Active and (any widgets where Active)) { assign N to 1 } }
        }
        """;

    [Test]
    public async Task ReadyAndAnyOpen_LoopNestsInsideIfOnLeftValue() {
        var (domain, analysis) = Evolve(BasketDsl);
        var order = domain.Types.OfType<Entity>().First(e => e.Name == "Order");
        var policy = order.Policies.First(p => p.Name == "ReadyWithOpenLine");
        var body = DomainToCSharpExporter.LowerExpressionToMethodBody(
            policy.Expression, order, domain, analysis);
        await Assert.That(body).IsTypeOf<Block>();
        var ifs = ((Block)body!).Nodes.OfType<IfStatement>().FirstOrDefault();
        await Assert.That(ifs).IsNotNull();
        await Assert.That(Flatten(ifs!.ThenBranch).OfType<ForEachLoop>().Any()).IsTrue();
        await Assert.That(Flatten(ifs.Condition).OfType<ForEachLoop>().Any()).IsFalse();
        var nodes = ((Block)body).Nodes;
        var assignIdx = nodes.ToList().FindIndex(n => n is Assignment);
        var ifIdx = nodes.ToList().FindIndex(n => n is IfStatement);
        await Assert.That(assignIdx).IsGreaterThanOrEqualTo(0);
        await Assert.That(assignIdx).IsLessThan(ifIdx);
    }

    [Test]
    public async Task ActionIfAny_SimulateAndGeneratedCSharp_Agree() {
        var (domain, analysis) = Evolve(BinDsl);
        await Assert.That(analysis.HasErrors).IsFalse();
        Entity E(string n) => domain.Types.OfType<Entity>().First(e => e.Name == n);
        var store = new DomainInstanceStore();
        var bin = DomainEntityInstance.Create(E("Bin"),
            new Dictionary<string, object?> { ["Active"] = false, ["N"] = 0L }, domain);
        var active = DomainEntityInstance.Create(E("Widget"),
            new Dictionary<string, object?> { ["Active"] = true }, domain);
        store.Add(bin); store.Add(active);
        store.Link("widgets", bin, active);
        var sim = bin.InvokeAction("Mark");
        await Assert.That(sim.Succeeded).IsTrue();
        await Assert.That(bin.GetProperty<bool>("Active")).IsTrue();

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis)));
        var widgetType = asm.GetType("Widget")!;
        var binType = asm.GetType("Bin")!;
        var widget = ExportedCSharp.CreateEntity(widgetType, ("active", true));
        var widgets = Array.CreateInstance(widgetType, 1);
        widgets.SetValue(widget, 0);
        var printed = ExportedCSharp.CreateEntity(binType, ("active", false), ("n", 0L), ("widgets", widgets));
        var result = binType.GetMethod("Mark")!.Invoke(printed, null)!;
        await Assert.That((bool)result.GetType().GetProperty("IsSuccess")!.GetValue(result)!).IsTrue();
        await Assert.That((bool)binType.GetProperty("Active")!.GetValue(printed)!).IsTrue();
    }

    private const string StagedBinDsl = """
        domain StagedBin
        Widget: entity { Active: Boolean default(false) }
        Bin: entity {
          N: Number default(0)
          widgets: many Widget
          Go: action { if (any widgets where Active) { assign N to 1 } }
          Open: stage {
            Go: action { if (any widgets where Active) { assign N to 2 } }
            Close: action { transition to Closed }
          }
          Closed: stage { }
        }
        """;

    [Test]
    public async Task StageDispatchedActionWithAny_SimulateAndGeneratedCSharp_Agree() {
        var (domain, analysis) = Evolve(StagedBinDsl);
        Entity E(string n) => domain.Types.OfType<Entity>().First(e => e.Name == n);

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis)));
        var widgetType = asm.GetType("Widget")!;
        var binType = asm.GetType("Bin")!;

        // Runs Go in Open, or in Closed (the entity-level body) after Close; returns N from both sides.
        async Task<(long Simulated, long Exported)> RunGo(bool closeFirst) {
            var store = new DomainInstanceStore();
            var bin = DomainEntityInstance.Create(E("Bin"), new Dictionary<string, object?>(), domain);
            var widget = DomainEntityInstance.Create(E("Widget"),
                new Dictionary<string, object?> { ["Active"] = true }, domain);
            store.Add(bin); store.Add(widget);
            store.Link("widgets", bin, widget);
            if (closeFirst)
                await Assert.That(bin.InvokeAction("Close").Succeeded).IsTrue();
            await Assert.That(bin.InvokeAction("Go").Succeeded).IsTrue();

            var widgets = Array.CreateInstance(widgetType, 1);
            widgets.SetValue(ExportedCSharp.CreateEntity(widgetType, ("active", true)), 0);
            var printed = ExportedCSharp.CreateEntity(binType, ("n", 0L), ("widgets", widgets));
            if (closeFirst)
                binType.GetMethod("Close")!.Invoke(printed, null);
            var result = binType.GetMethod("Go")!.Invoke(printed, null)!;
            await Assert.That((bool)result.GetType().GetProperty("IsSuccess")!.GetValue(result)!).IsTrue();

            return (Convert.ToInt64(bin.GetProperty<object>("N")),
                Convert.ToInt64(binType.GetProperty("N")!.GetValue(printed)));
        }

        var open = await RunGo(closeFirst: false);
        await Assert.That(open.Simulated).IsEqualTo(2L);
        await Assert.That(open.Exported).IsEqualTo(open.Simulated);

        var closed = await RunGo(closeFirst: true);
        await Assert.That(closed.Simulated).IsEqualTo(1L);
        await Assert.That(closed.Exported).IsEqualTo(closed.Simulated);
    }

    [Test]
    public async Task ActionAssignCountWhere_SimulateAndGeneratedCSharp_Agree() {
        var (domain, analysis) = Evolve(BinDsl);
        await Assert.That(analysis.HasErrors).IsFalse();
        Entity E(string n) => domain.Types.OfType<Entity>().First(e => e.Name == n);
        var store = new DomainInstanceStore();
        var bin = DomainEntityInstance.Create(E("Bin"),
            new Dictionary<string, object?> { ["Active"] = false, ["N"] = 0L }, domain);
        var w1 = DomainEntityInstance.Create(E("Widget"),
            new Dictionary<string, object?> { ["Active"] = true }, domain);
        var w2 = DomainEntityInstance.Create(E("Widget"),
            new Dictionary<string, object?> { ["Active"] = false }, domain);
        store.Add(bin); store.Add(w1); store.Add(w2);
        store.Link("widgets", bin, w1);
        store.Link("widgets", bin, w2);
        var sim = bin.InvokeAction("Tally");
        await Assert.That(sim.Succeeded).IsTrue();
        await Assert.That(Convert.ToInt64(bin.GetProperty<object>("N"))).IsEqualTo(1L);

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis)));
        var widgetType = asm.GetType("Widget")!;
        var binType = asm.GetType("Bin")!;
        var printedWidgets = Array.CreateInstance(widgetType, 2);
        printedWidgets.SetValue(ExportedCSharp.CreateEntity(widgetType, ("active", true)), 0);
        printedWidgets.SetValue(ExportedCSharp.CreateEntity(widgetType, ("active", false)), 1);
        var printed = ExportedCSharp.CreateEntity(binType, ("active", false), ("n", 0L), ("widgets", printedWidgets));
        var result = binType.GetMethod("Tally")!.Invoke(printed, null)!;
        await Assert.That((bool)result.GetType().GetProperty("IsSuccess")!.GetValue(result)!).IsTrue();
        await Assert.That(Convert.ToInt64(binType.GetProperty("N")!.GetValue(printed))).IsEqualTo(1L);
    }

    [Test]
    public async Task CreateInitializer_QuantifierValue_SimulateAndGeneratedCSharp_Agree() {
        var poly = """
            domain Yard
            Widget: entity { Active: Boolean default(false) }
            Bin: entity {
              Active: Boolean default(false)
              widgets: many Widget
              Spawn: action { create in widgets { Active: any widgets where Active } }
            }
            """;
        var (domain, analysis) = Evolve(poly);
        Entity E(string n) => domain.Types.OfType<Entity>().First(e => e.Name == n);
        var store = new DomainInstanceStore();
        var bin = DomainEntityInstance.Create(E("Bin"),
            new Dictionary<string, object?> { ["Active"] = false }, domain);
        var active = DomainEntityInstance.Create(E("Widget"),
            new Dictionary<string, object?> { ["Active"] = true }, domain);
        store.Add(bin); store.Add(active);
        store.Link("widgets", bin, active);
        var sim = bin.InvokeAction("Spawn");
        await Assert.That(sim.Succeeded).IsTrue();
        await Assert.That(store.GetLinkedTargets("widgets", bin).Count).IsEqualTo(2);

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis)));
        var widgetType = asm.GetType("Widget")!;
        var binType = asm.GetType("Bin")!;
        var widgets = Array.CreateInstance(widgetType, 1);
        widgets.SetValue(ExportedCSharp.CreateEntity(widgetType, ("active", true)), 0);
        var printed = ExportedCSharp.CreateEntity(binType, ("active", false), ("widgets", widgets));
        var result = binType.GetMethod("Spawn")!.Invoke(printed, null)!;
        await Assert.That((bool)result.GetType().GetProperty("IsSuccess")!.GetValue(result)!).IsTrue();
        var printedWidgets = binType.GetProperty("Widgets")!.GetValue(printed) as System.Collections.ICollection;
        await Assert.That(printedWidgets).IsNotNull();
        await Assert.That(printedWidgets!.Count).IsEqualTo(2);
        var activeCount = 0;
        foreach (var item in printedWidgets)
            if ((bool)widgetType.GetProperty("Active")!.GetValue(item)!)
                activeCount++;
        await Assert.That(activeCount).IsEqualTo(2);
    }

    [Test]
    public async Task ActionShortCircuitAnd_SimulateAndGeneratedCSharp_Agree() {
        var (domain, analysis) = Evolve(BinDsl);
        await Assert.That(analysis.HasErrors).IsFalse();
        Entity E(string n) => domain.Types.OfType<Entity>().First(e => e.Name == n);
        var store = new DomainInstanceStore();
        var bin = DomainEntityInstance.Create(E("Bin"),
            new Dictionary<string, object?> { ["Active"] = false, ["N"] = 0L }, domain);
        var active = DomainEntityInstance.Create(E("Widget"),
            new Dictionary<string, object?> { ["Active"] = true }, domain);
        store.Add(bin); store.Add(active);
        store.Link("widgets", bin, active);
        var sim = bin.InvokeAction("Guarded");
        await Assert.That(sim.Succeeded).IsTrue();
        await Assert.That(Convert.ToInt64(bin.GetProperty<object>("N"))).IsEqualTo(0L);

        var lowered = new EffectLoweringPass(E("Bin"), new LoweringContext(
            new Parameter("entity", new TypeReference("Bin")),
            Analysis: analysis,
            Domain: domain,
            UseThisReference: true)).LowerActionBody(E("Bin").Actions.First(a => a.Name == "Guarded").Effects);
        var ifs = Flatten(lowered!).OfType<IfStatement>().First(s =>
            Flatten(s.ThenBranch).OfType<ForEachLoop>().Any());
        await Assert.That(Flatten(ifs.Condition).OfType<ForEachLoop>().Any()).IsFalse();

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis)));
        var widgetType = asm.GetType("Widget")!;
        var binType = asm.GetType("Bin")!;
        var widgets = Array.CreateInstance(widgetType, 1);
        widgets.SetValue(ExportedCSharp.CreateEntity(widgetType, ("active", true)), 0);
        var printed = ExportedCSharp.CreateEntity(binType, ("active", false), ("n", 0L), ("widgets", widgets));
        var result = binType.GetMethod("Guarded")!.Invoke(printed, null)!;
        await Assert.That((bool)result.GetType().GetProperty("IsSuccess")!.GetValue(result)!).IsTrue();
        await Assert.That(Convert.ToInt64(binType.GetProperty("N")!.GetValue(printed))).IsEqualTo(0L);
    }

    [Test]
    public async Task ActionIfAny_ThenForEachInvokeArgAny_SimulateAndGeneratedCSharp_Agree() {
        var poly = """
            domain Yard
            Widget: entity {
              Active: Boolean default(false)
              Flag: Boolean default(false)
              Mark: action (flag: Boolean) { assign Flag to flag }
            }
            Bin: entity {
              N: Number default(0)
              widgets: many Widget
              Go: action {
                if (any widgets where Active) { assign N to 1 }
                for widgets as w invoke w.Mark(flag: any widgets where Active)
              }
            }
            """;
        var (domain, analysis) = Evolve(poly);
        await Assert.That(analysis.HasErrors).IsFalse();
        Entity E(string n) => domain.Types.OfType<Entity>().First(e => e.Name == n);
        var store = new DomainInstanceStore();
        var bin = DomainEntityInstance.Create(E("Bin"),
            new Dictionary<string, object?> { ["N"] = 0L }, domain);
        var active = DomainEntityInstance.Create(E("Widget"),
            new Dictionary<string, object?> { ["Active"] = true, ["Flag"] = false }, domain);
        var idle = DomainEntityInstance.Create(E("Widget"),
            new Dictionary<string, object?> { ["Active"] = false, ["Flag"] = false }, domain);
        store.Add(bin); store.Add(active); store.Add(idle);
        store.Link("widgets", bin, active);
        store.Link("widgets", bin, idle);
        var sim = bin.InvokeAction("Go");
        await Assert.That(sim.Succeeded).IsTrue();
        await Assert.That(Convert.ToInt64(bin.GetProperty<object>("N"))).IsEqualTo(1L);
        await Assert.That(active.GetProperty<bool>("Flag")).IsTrue();
        await Assert.That(idle.GetProperty<bool>("Flag")).IsTrue();

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis)));
        var widgetType = asm.GetType("Widget")!;
        var binType = asm.GetType("Bin")!;
        var printedWidgets = Array.CreateInstance(widgetType, 2);
        printedWidgets.SetValue(ExportedCSharp.CreateEntity(widgetType, ("active", true), ("flag", false)), 0);
        printedWidgets.SetValue(ExportedCSharp.CreateEntity(widgetType, ("active", false), ("flag", false)), 1);
        var printed = ExportedCSharp.CreateEntity(binType, ("n", 0L), ("widgets", printedWidgets));
        var result = binType.GetMethod("Go")!.Invoke(printed, null)!;
        await Assert.That((bool)result.GetType().GetProperty("IsSuccess")!.GetValue(result)!).IsTrue();
        await Assert.That(Convert.ToInt64(binType.GetProperty("N")!.GetValue(printed))).IsEqualTo(1L);
        var after = (System.Collections.ICollection)binType.GetProperty("Widgets")!.GetValue(printed)!;
        foreach (var item in after)
            await Assert.That((bool)widgetType.GetProperty("Flag")!.GetValue(item)!).IsTrue();
    }

    [Test]
    public async Task CreateInReturn_QuantifierInitializer_SimulateAndGeneratedCSharp_Agree() {
        var poly = """
            domain Yard
            Widget: entity { Active: Boolean default(false) }
            Bin: entity {
              widgets: many Widget
              Spawn: action -> Widget { create in widgets { Active: any widgets where Active } }
            }
            """;
        var (domain, analysis) = Evolve(poly);
        await Assert.That(analysis.HasErrors).IsFalse();
        Entity E(string n) => domain.Types.OfType<Entity>().First(e => e.Name == n);
        var store = new DomainInstanceStore();
        var bin = DomainEntityInstance.Create(E("Bin"), domain: domain);
        var active = DomainEntityInstance.Create(E("Widget"),
            new Dictionary<string, object?> { ["Active"] = true }, domain);
        store.Add(bin); store.Add(active);
        store.Link("widgets", bin, active);
        var sim = bin.InvokeAction("Spawn");
        await Assert.That(sim.Succeeded).IsTrue();
        await Assert.That(sim.ResultInstance).IsNotNull();
        var simActive = sim.ResultInstance!.GetProperty<bool>("Active");

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis)));
        var widgetType = asm.GetType("Widget")!;
        var binType = asm.GetType("Bin")!;
        var widgets = Array.CreateInstance(widgetType, 1);
        widgets.SetValue(ExportedCSharp.CreateEntity(widgetType, ("active", true)), 0);
        var printed = ExportedCSharp.CreateEntity(binType, ("widgets", widgets));
        var result = binType.GetMethod("Spawn")!.Invoke(printed, null)!;
        await Assert.That((bool)result.GetType().GetProperty("IsSuccess")!.GetValue(result)!).IsTrue();
        var printedWidget = result.GetType().GetProperty("Value")!.GetValue(result)!;
        await Assert.That((bool)widgetType.GetProperty("Active")!.GetValue(printedWidget)!).IsEqualTo(simActive);
        await Assert.That(simActive).IsTrue();
    }

    [Test]
    public async Task PathPrefixHop_QuantifierOnTarget_SimulateAndGeneratedCSharp_Agree() {
        var poly = """
            domain Shop
            Worker: entity { Active: Boolean }
            Team: entity {
              Active: Boolean
              workers: many Worker
            }
            Dept: entity {
              team: Team
              P: policy { team Active == any workers where Active }
            }
            """;
        var (domain, analysis) = Evolve(poly);
        await Assert.That(analysis.HasErrors).IsFalse();
        Entity E(string n) => domain.Types.OfType<Entity>().First(e => e.Name == n);
        var store = new DomainInstanceStore();
        DomainEntityInstance SimDept(bool teamActive, bool workerActive) {
            var worker = DomainEntityInstance.Create(E("Worker"),
                new Dictionary<string, object?> { ["Active"] = workerActive }, domain);
            var team = DomainEntityInstance.Create(E("Team"),
                new Dictionary<string, object?> { ["Active"] = teamActive }, domain);
            var dept = DomainEntityInstance.Create(E("Dept"), domain: domain);
            store.Add(worker); store.Add(team); store.Add(dept);
            store.Link("workers", team, worker);
            store.Link("team", dept, team);
            return dept;
        }
        var policy = E("Dept").Policies.First(p => p.Name == "P");
        var simTrue = SimDept(true, true).EvaluatePolicy(policy);
        var simFalse = SimDept(true, false).EvaluatePolicy(policy);
        await Assert.That(simTrue).IsTrue();
        await Assert.That(simFalse).IsFalse();

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis)));
        var workerType = asm.GetType("Worker")!;
        var teamType = asm.GetType("Team")!;
        var deptType = asm.GetType("Dept")!;
        object PrintDept(bool teamActive, bool workerActive) {
            var workers = Array.CreateInstance(workerType, 1);
            workers.SetValue(ExportedCSharp.CreateEntity(workerType, ("active", workerActive)), 0);
            var team = ExportedCSharp.CreateEntity(teamType, ("active", teamActive), ("workers", workers));
            return ExportedCSharp.CreateEntity(deptType, ("team", team));
        }
        bool PrintedP(object dept) => (bool)deptType.GetMethod("P")!.Invoke(dept, null)!;
        await Assert.That(PrintedP(PrintDept(true, true))).IsEqualTo(simTrue);
        await Assert.That(PrintedP(PrintDept(true, false))).IsEqualTo(simFalse);
    }

    [Test]
    public async Task DatePlusCountWhere_SimulateAndGeneratedCSharp_Agree() {
        var poly = """
            domain Jobs
            uses temporal
            Line: entity { Open: Boolean }
            Ticket: entity {
              Due: Date
              lines: many Line
              P: policy { (Due + count lines where Open) > Due }
            }
            """;
        var (domain, analysis) = Evolve(poly);
        await Assert.That(analysis.HasErrors).IsFalse();
        Entity E(string n) => domain.Types.OfType<Entity>().First(e => e.Name == n);
        var due = new DateOnly(2026, 1, 1);
        var store = new DomainInstanceStore();
        DomainEntityInstance SimTicket(bool openLine) {
            var ticket = DomainEntityInstance.Create(E("Ticket"),
                new Dictionary<string, object?> { ["Due"] = due }, domain);
            store.Add(ticket);
            if (openLine) {
                var line = DomainEntityInstance.Create(E("Line"),
                    new Dictionary<string, object?> { ["Open"] = true }, domain);
                store.Add(line);
                store.Link("lines", ticket, line);
            }
            return ticket;
        }
        var policy = E("Ticket").Policies.First(p => p.Name == "P");
        var simOpen = SimTicket(true).EvaluatePolicy(policy);
        var simClosed = SimTicket(false).EvaluatePolicy(policy);
        await Assert.That(simOpen).IsTrue();
        await Assert.That(simClosed).IsFalse();

        var cs = new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis));
        var asm = ExportedCSharp.CompileAndLoad(cs);
        var lineType = asm.GetType("Line")!;
        var ticketType = asm.GetType("Ticket")!;
        object PrintTicket(bool openLine) {
            Array lines;
            if (openLine) {
                lines = Array.CreateInstance(lineType, 1);
                lines.SetValue(ExportedCSharp.CreateEntity(lineType, ("open", true)), 0);
            }
            else {
                lines = Array.CreateInstance(lineType, 0);
            }
            return ExportedCSharp.CreateEntity(ticketType, ("due", due), ("lines", lines));
        }
        bool PrintedP(object ticket) => (bool)ticketType.GetMethod("P")!.Invoke(ticket, null)!;
        await Assert.That(PrintedP(PrintTicket(true))).IsEqualTo(simOpen);
        await Assert.That(PrintedP(PrintTicket(false))).IsEqualTo(simClosed);
    }

    [Test]
    public async Task QuantifierBody_ReadsActionParameterNotTargetProperty_SimulateAndPrintAgree() {
        var poly = """
            domain Yard
            Widget: entity { Level: Number }
            Bin: entity {
              N: Number default(0)
              widgets: many Widget
              Close: action (Level: Number) {
                if (any widgets where Level > 3) { assign N to 1 }
              }
            }
            """;
        var (domain, analysis) = Evolve(poly);
        await Assert.That(analysis.HasErrors).IsFalse();
        Entity E(string n) => domain.Types.OfType<Entity>().First(e => e.Name == n);
        var store = new DomainInstanceStore();
        var bin = DomainEntityInstance.Create(E("Bin"),
            new Dictionary<string, object?> { ["N"] = 0L }, domain);
        var widget = DomainEntityInstance.Create(E("Widget"),
            new Dictionary<string, object?> { ["Level"] = 0L }, domain);
        store.Add(bin); store.Add(widget);
        store.Link("widgets", bin, widget);
        var simHigh = bin.InvokeAction("Close", new Dictionary<string, object?> { ["Level"] = 10L });
        await Assert.That(simHigh.Succeeded).IsTrue();
        await Assert.That(Convert.ToInt64(bin.GetProperty<object>("N"))).IsEqualTo(1L);

        var binLow = DomainEntityInstance.Create(E("Bin"),
            new Dictionary<string, object?> { ["N"] = 0L }, domain);
        var widgetLow = DomainEntityInstance.Create(E("Widget"),
            new Dictionary<string, object?> { ["Level"] = 0L }, domain);
        store.Add(binLow); store.Add(widgetLow);
        store.Link("widgets", binLow, widgetLow);
        var simLow = binLow.InvokeAction("Close", new Dictionary<string, object?> { ["Level"] = 2L });
        await Assert.That(simLow.Succeeded).IsTrue();
        await Assert.That(Convert.ToInt64(binLow.GetProperty<object>("N"))).IsEqualTo(0L);

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis)));
        var widgetType = asm.GetType("Widget")!;
        var binType = asm.GetType("Bin")!;
        object PrintBin() {
            var widgets = Array.CreateInstance(widgetType, 1);
            widgets.SetValue(ExportedCSharp.CreateEntity(widgetType, ("level", 0L)), 0);
            return ExportedCSharp.CreateEntity(binType, ("n", 0L), ("widgets", widgets));
        }
        var close = binType.GetMethod("Close")!;
        var printedHigh = PrintBin();
        var highResult = close.Invoke(printedHigh, [10L])!;
        await Assert.That((bool)highResult.GetType().GetProperty("IsSuccess")!.GetValue(highResult)!).IsTrue();
        await Assert.That(Convert.ToInt64(binType.GetProperty("N")!.GetValue(printedHigh))).IsEqualTo(1L);
        var printedLow = PrintBin();
        var lowResult = close.Invoke(printedLow, [2L])!;
        await Assert.That((bool)lowResult.GetType().GetProperty("IsSuccess")!.GetValue(lowResult)!).IsTrue();
        await Assert.That(Convert.ToInt64(binType.GetProperty("N")!.GetValue(printedLow))).IsEqualTo(0L);
    }

    [Test]
    public async Task ForEachInvoke_QuantifierPredicate_SimulateAndGeneratedCSharp_Agree() {
        var poly = """
            domain Yard
            Part: entity { Open: Boolean }
            Widget: entity {
              Flag: Boolean default(false)
              parts: many Part
              HasOpenPart: policy { any parts where Open }
              Mark: action { assign Flag to true }
            }
            Bin: entity {
              widgets: many Widget
              Go: action {
                for widgets as w where w HasOpenPart invoke w.Mark()
              }
            }
            """;
        var (domain, analysis) = Evolve(poly);
        await Assert.That(analysis.HasErrors).IsFalse();
        Entity E(string n) => domain.Types.OfType<Entity>().First(e => e.Name == n);
        var store = new DomainInstanceStore();
        var openPart = DomainEntityInstance.Create(E("Part"),
            new Dictionary<string, object?> { ["Open"] = true }, domain);
        var closedPart = DomainEntityInstance.Create(E("Part"),
            new Dictionary<string, object?> { ["Open"] = false }, domain);
        var withOpen = DomainEntityInstance.Create(E("Widget"),
            new Dictionary<string, object?> { ["Flag"] = false }, domain);
        var withoutOpen = DomainEntityInstance.Create(E("Widget"),
            new Dictionary<string, object?> { ["Flag"] = false }, domain);
        var bin = DomainEntityInstance.Create(E("Bin"), domain: domain);
        store.Add(openPart); store.Add(closedPart); store.Add(withOpen); store.Add(withoutOpen); store.Add(bin);
        store.Link("parts", withOpen, openPart);
        store.Link("parts", withoutOpen, closedPart);
        store.Link("widgets", bin, withOpen);
        store.Link("widgets", bin, withoutOpen);
        var sim = bin.InvokeAction("Go");
        await Assert.That(sim.Succeeded).IsTrue();
        await Assert.That(withOpen.GetProperty<bool>("Flag")).IsTrue();
        await Assert.That(withoutOpen.GetProperty<bool>("Flag")).IsFalse();

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis)));
        var partType = asm.GetType("Part")!;
        var widgetType = asm.GetType("Widget")!;
        var binType = asm.GetType("Bin")!;
        Array Parts(bool open) {
            var parts = Array.CreateInstance(partType, 1);
            parts.SetValue(ExportedCSharp.CreateEntity(partType, ("open", open)), 0);
            return parts;
        }
        var printedWidgets = Array.CreateInstance(widgetType, 2);
        printedWidgets.SetValue(ExportedCSharp.CreateEntity(widgetType, ("flag", false), ("parts", Parts(true))), 0);
        printedWidgets.SetValue(ExportedCSharp.CreateEntity(widgetType, ("flag", false), ("parts", Parts(false))), 1);
        var printed = ExportedCSharp.CreateEntity(binType, ("widgets", printedWidgets));
        var result = binType.GetMethod("Go")!.Invoke(printed, null)!;
        await Assert.That((bool)result.GetType().GetProperty("IsSuccess")!.GetValue(result)!).IsTrue();
        var after = (System.Collections.ICollection)binType.GetProperty("Widgets")!.GetValue(printed)!;
        var flags = new List<bool>();
        foreach (var item in after)
            flags.Add((bool)widgetType.GetProperty("Flag")!.GetValue(item)!);
        await Assert.That(flags.Count).IsEqualTo(2);
        await Assert.That(flags[0]).IsTrue();
        await Assert.That(flags[1]).IsFalse();
    }

    private static IEnumerable<Node> Flatten(Node node) {
        yield return node;
        foreach (var child in node.Children) {
            if (child is null) continue;
            foreach (var n in Flatten(child))
                yield return n;
        }
    }
}
