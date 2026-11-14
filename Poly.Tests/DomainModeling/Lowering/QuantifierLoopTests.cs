using Poly.Ast.Nodes;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

public class QuantifierLoopTests {
    private static (Domain Domain, AnalysisResult Analysis) Evolve(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
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
        var exec = ModuleExec.For(domain);
        var order = domain.Types.OfType<Entity>().First(e => e.Name == "Order");
        var inst = exec.Create((order).Name, new Dictionary<string, object?> { ["Ready"] = true }).Instance!;
        await Assert.That(exec.EvaluatePolicy(inst, (order.Policies.First(p => p.Name == "AllOpen")).Name)).IsFalse();
        await Assert.That(exec.EvaluatePolicy(inst, (order.Policies.First(p => p.Name == "AnyOpen")).Name)).IsFalse();
        await Assert.That(exec.EvaluatePolicy(inst, (order.Policies.First(p => p.Name == "NoneOpen")).Name)).IsTrue();
    }

    [Test]
    public async Task Simulate_None_And_CountWhere_MatchLinkedLines() {
        var (domain, _) = Evolve(BasketDsl);
        var exec = ModuleExec.For(domain);
        var orderEnt = domain.Types.OfType<Entity>().First(e => e.Name == "Order");
        var lineEnt = domain.Types.OfType<Entity>().First(e => e.Name == "Line");
        var order = exec.Create((orderEnt).Name, new Dictionary<string, object?> { ["Ready"] = true }).Instance!;
        var openA = exec.Create((lineEnt).Name, new Dictionary<string, object?> { ["Qty"] = 1L, ["Open"] = true }).Instance!;
        var openB = exec.Create((lineEnt).Name, new Dictionary<string, object?> { ["Qty"] = 2L, ["Open"] = true }).Instance!;
        var closed = exec.Create((lineEnt).Name, new Dictionary<string, object?> { ["Qty"] = 3L, ["Open"] = false }).Instance!;
        exec.Link("lines", order, openA);
        exec.Link("lines", order, openB);
        exec.Link("lines", order, closed);

        await Assert.That(exec.EvaluatePolicy(order, (orderEnt.Policies.First(p => p.Name == "AnyOpen")).Name)).IsTrue();
        await Assert.That(exec.EvaluatePolicy(order, (orderEnt.Policies.First(p => p.Name == "AllOpen")).Name)).IsFalse();
        await Assert.That(exec.EvaluatePolicy(order, (orderEnt.Policies.First(p => p.Name == "NoneOpen")).Name)).IsFalse();
        await Assert.That(exec.EvaluatePolicy(order, (orderEnt.Policies.First(p => p.Name == "OpenCount")).Name)).IsTrue();
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
        var types = DomainProgramProjection.ToSyntax(domain, analysis);
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
        var exec = ModuleExec.For(domain);
        Entity E(string name) => domain.Types.OfType<Entity>().First(e => e.Name == name);
        var policyNames = E("Order").Policies.Select(p => p.Name).ToArray();

        // Simulated: instances linked through a store.
        ModuleInstance SimPart(long qty) {
            var part = exec.Create("Part", new Dictionary<string, object?> { ["Qty"] = qty }).Instance!;
            return part;
        }
        ModuleInstance SimLine(bool open, params ModuleInstance[] parts) {
            var line = exec.Create("Line", new Dictionary<string, object?> { ["Qty"] = 1L, ["Open"] = open }).Instance!;
            foreach (var part in parts) exec.Link("parts", line, part);
            return line;
        }
        ModuleInstance SimOrder(bool ready, params ModuleInstance[] lines) {
            var order = exec.Create("Order", new Dictionary<string, object?> { ["Ready"] = ready }).Instance!;
            foreach (var line in lines) exec.Link("lines", order, line);
            return order;
        }

        // Printed: the same shapes built from the exported, compiled types.
        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis)));
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

        var cases = new (string Name, ModuleInstance Simulated, object Printed)[] {
            ("no lines", SimOrder(true), PrintOrder(true)),
            ("mixed lines", SimOrder(true, SimLine(true, SimPart(9)), SimLine(true), SimLine(false)),
                PrintOrder(true, PrintLine(true, PrintPart(9)), PrintLine(true), PrintLine(false))),
            ("all open, small parts, not ready", SimOrder(false, SimLine(true, SimPart(1)), SimLine(true)),
                PrintOrder(false, PrintLine(true, PrintPart(1)), PrintLine(true))),
        };
        var bigPart = E("Order").Policies.First(p => p.Name == "AnyOpenLineWithBigPart");
        await Assert.That(exec.EvaluatePolicy(cases[1].Simulated, bigPart.Name)).IsTrue();
        await Assert.That(exec.EvaluatePolicy(cases[2].Simulated, bigPart.Name)).IsFalse();

        foreach (var (name, simulated, printed) in cases) {
            foreach (var policyName in policyNames) {
                var sim = exec.EvaluatePolicy(simulated, (E("Order").Policies.First(p => p.Name == policyName)).Name);
                var print = (bool)printed.GetType().GetMethod(policyName, PrintedMembers.Any)!.Invoke(printed, null)!;
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
        var exec = ModuleExec.For(domain);
        await Assert.That(analysis.HasErrors).IsFalse();
        var bin = exec.Create("Bin", new Dictionary<string, object?> { ["Active"] = false, ["N"] = 0L }).Instance!;
        var active = exec.Create("Widget", new Dictionary<string, object?> { ["Active"] = true }).Instance!;
        exec.Link("widgets", bin, active);
        var sim = exec.Invoke(bin, "Mark");
        await Assert.That(sim.Succeeded).IsTrue();
        await Assert.That((bool)bin.Bag["Active"]!).IsTrue();

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis)));
        var widgetType = asm.GetType("Widget")!;
        var binType = asm.GetType("Bin")!;
        var widget = ExportedCSharp.CreateEntity(widgetType, ("active", true));
        var widgets = Array.CreateInstance(widgetType, 1);
        widgets.SetValue(widget, 0);
        var printed = ExportedCSharp.CreateEntity(binType, ("active", false), ("n", 0L), ("widgets", widgets));
        var result = binType.GetMethod("Mark", PrintedMembers.Any)!.Invoke(printed, null)!;
        await Assert.That((bool)result.GetType().GetProperty("IsSuccess", PrintedMembers.Any)!.GetValue(result)!).IsTrue();
        await Assert.That((bool)binType.GetProperty("Active", PrintedMembers.Any)!.GetValue(printed)!).IsTrue();
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
        var exec = ModuleExec.For(domain);

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis)));
        var widgetType = asm.GetType("Widget")!;
        var binType = asm.GetType("Bin")!;

        // Runs Go in Open, or in Closed (the entity-level body) after Close; returns N from both sides.
        async Task<(long Simulated, long Exported)> RunGo(bool closeFirst) {
            var bin = exec.Create("Bin", new Dictionary<string, object?>()).Instance!;
            var widget = exec.Create("Widget", new Dictionary<string, object?> { ["Active"] = true }).Instance!;
            exec.Link("widgets", bin, widget);
            if (closeFirst)
                await Assert.That(exec.Invoke(bin, "Close").Succeeded).IsTrue();
            await Assert.That(exec.Invoke(bin, "Go").Succeeded).IsTrue();

            var widgets = Array.CreateInstance(widgetType, 1);
            widgets.SetValue(ExportedCSharp.CreateEntity(widgetType, ("active", true)), 0);
            var printed = ExportedCSharp.CreateEntity(binType, ("n", 0L), ("widgets", widgets));
            if (closeFirst)
                binType.GetMethod("Close", PrintedMembers.Any)!.Invoke(printed, null);
            var result = binType.GetMethod("Go", PrintedMembers.Any)!.Invoke(printed, null)!;
            await Assert.That((bool)result.GetType().GetProperty("IsSuccess", PrintedMembers.Any)!.GetValue(result)!).IsTrue();

            return (Convert.ToInt64(bin.Bag["N"]),
                Convert.ToInt64(binType.GetProperty("N", PrintedMembers.Any)!.GetValue(printed)));
        }

        var open = await RunGo(closeFirst: false);
        await Assert.That(open.Simulated).IsEqualTo(2L);
        await Assert.That(open.Exported).IsEqualTo(open.Simulated);

        var closed = await RunGo(closeFirst: true);
        await Assert.That(closed.Simulated).IsEqualTo(1L);
        await Assert.That(closed.Exported).IsEqualTo(closed.Simulated);
    }

    private const string ExistsPredicateDsl = """
        domain Yard
        Owner: entity { Label: Text }
        Part: entity { Qty: Number }
        Widget: entity {
          Flag: Boolean default(false)
          owner: Owner optional
          parts: many Part
          HasOwner: policy { owner exists }
          HasParts: policy { parts exists }
          Mark: action { assign Flag to true }
        }
        Bin: entity {
          widgets: many Widget
          MarkOwned: action { for widgets as w where w HasOwner invoke w.Mark() }
          MarkWithParts: action { for widgets as w where w HasParts invoke w.Mark() }
        }
        """;

    [Test]
    public async Task ForEachRelationshipExistsPredicate_SimulateAndGeneratedCSharp_Agree() {
        var (domain, analysis) = Evolve(ExistsPredicateDsl);
        var exec = ModuleExec.For(domain);
        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis)));
        var widgetType = asm.GetType("Widget")!;
        var binType = asm.GetType("Bin")!;

        // Two widgets: "bare" has no links; "linked" has an owner and one part.
        // Returns each widget's Flag after the action, per side.
        async Task<(bool[] Simulated, bool[] Exported)> Run(string actionName) {
            var bin = exec.Create("Bin", new Dictionary<string, object?>()).Instance!;
            var bare = exec.Create("Widget", new Dictionary<string, object?>()).Instance!;
            var linked = exec.Create("Widget", new Dictionary<string, object?>()).Instance!;
            var owner = exec.Create("Owner", new Dictionary<string, object?> { ["Label"] = "o" }).Instance!;
            var part = exec.Create("Part", new Dictionary<string, object?> { ["Qty"] = 1L }).Instance!;
            exec.Link("widgets", bin, bare);
            exec.Link("widgets", bin, linked);
            exec.Link("owner", linked, owner);
            exec.Link("parts", linked, part);
            await Assert.That(exec.Invoke(bin, actionName).Succeeded).IsTrue();

            var printedParts = Array.CreateInstance(asm.GetType("Part")!, 1);
            printedParts.SetValue(ExportedCSharp.CreateEntity(asm, "Part", ("qty", 1L)), 0);
            var printedBare = ExportedCSharp.CreateEntity(widgetType);
            var printedLinked = ExportedCSharp.CreateEntity(widgetType,
                ("owner", ExportedCSharp.CreateEntity(asm, "Owner", ("label", "o"))), ("parts", printedParts));
            var widgets = Array.CreateInstance(widgetType, 2);
            widgets.SetValue(printedBare, 0);
            widgets.SetValue(printedLinked, 1);
            var printed = ExportedCSharp.CreateEntity(binType, ("widgets", widgets));
            var result = binType.GetMethod(actionName, PrintedMembers.Any)!.Invoke(printed, null)!;
            await Assert.That((bool)result.GetType().GetProperty("IsSuccess", PrintedMembers.Any)!.GetValue(result)!).IsTrue();

            bool Flag(object w) => (bool)widgetType.GetProperty("Flag", PrintedMembers.Any)!.GetValue(w)!;
            return ([(bool)bare.Bag["Flag"]!, (bool)linked.Bag["Flag"]!],
                [Flag(printedBare), Flag(printedLinked)]);
        }

        var byOwner = await Run("MarkOwned");
        await Assert.That(byOwner.Simulated).IsEquivalentTo(new[] { false, true });
        await Assert.That(byOwner.Exported).IsEquivalentTo(byOwner.Simulated);

        var byParts = await Run("MarkWithParts");
        await Assert.That(byParts.Simulated).IsEquivalentTo(new[] { false, true });
        await Assert.That(byParts.Exported).IsEquivalentTo(byParts.Simulated);
    }

    [Test]
    public async Task NumberValuedPolicy_SimulatesAsNonzero_AndPrintDoesNotCompile() {
        // Analysis accepts a policy whose body is a Number. Simulate treats any
        // nonzero value as true; the printed `bool P() => this.N` is a C# type error.
        var (domain, analysis) = Evolve("""
            domain Yard
            Bin: entity { N: Number default(0)  P: policy { N } }
            """);
        var exec = ModuleExec.For(domain);
        var bin = exec.Create((domain.Types.OfType<Entity>().First(e => e.Name == "Bin")).Name, new Dictionary<string, object?> { ["N"] = 2L }).Instance!;
        var policy = domain.Types.OfType<Entity>().First(e => e.Name == "Bin").Policies.First(p => p.Name == "P");
        await Assert.That(exec.EvaluatePolicy(bin, (policy).Name)).IsTrue();

        var cs = new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis));
        await Assert.That(() => ExportedCSharp.CompileAndLoad(cs)).Throws<InvalidOperationException>()
            .WithMessageContaining("CS0029");
    }

    [Test]
    public async Task ActionAssignCountWhere_SimulateAndGeneratedCSharp_Agree() {
        var (domain, analysis) = Evolve(BinDsl);
        var exec = ModuleExec.For(domain);
        await Assert.That(analysis.HasErrors).IsFalse();
        var bin = exec.Create("Bin", new Dictionary<string, object?> { ["Active"] = false, ["N"] = 0L }).Instance!;
        var w1 = exec.Create("Widget", new Dictionary<string, object?> { ["Active"] = true }).Instance!;
        var w2 = exec.Create("Widget", new Dictionary<string, object?> { ["Active"] = false }).Instance!;
        exec.Link("widgets", bin, w1);
        exec.Link("widgets", bin, w2);
        var sim = exec.Invoke(bin, "Tally");
        await Assert.That(sim.Succeeded).IsTrue();
        await Assert.That(Convert.ToInt64(bin.Bag["N"])).IsEqualTo(1L);

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis)));
        var widgetType = asm.GetType("Widget")!;
        var binType = asm.GetType("Bin")!;
        var printedWidgets = Array.CreateInstance(widgetType, 2);
        printedWidgets.SetValue(ExportedCSharp.CreateEntity(widgetType, ("active", true)), 0);
        printedWidgets.SetValue(ExportedCSharp.CreateEntity(widgetType, ("active", false)), 1);
        var printed = ExportedCSharp.CreateEntity(binType, ("active", false), ("n", 0L), ("widgets", printedWidgets));
        var result = binType.GetMethod("Tally", PrintedMembers.Any)!.Invoke(printed, null)!;
        await Assert.That((bool)result.GetType().GetProperty("IsSuccess", PrintedMembers.Any)!.GetValue(result)!).IsTrue();
        await Assert.That(Convert.ToInt64(binType.GetProperty("N", PrintedMembers.Any)!.GetValue(printed))).IsEqualTo(1L);
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
        var exec = ModuleExec.For(domain);
        var bin = exec.Create("Bin", new Dictionary<string, object?> { ["Active"] = false }).Instance!;
        var active = exec.Create("Widget", new Dictionary<string, object?> { ["Active"] = true }).Instance!;
        exec.Link("widgets", bin, active);
        var sim = exec.Invoke(bin, "Spawn");
        await Assert.That(sim.Succeeded).IsTrue();
        await Assert.That(exec.LinkedTargets("widgets", bin).Count).IsEqualTo(2);

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis)));
        var widgetType = asm.GetType("Widget")!;
        var binType = asm.GetType("Bin")!;
        var widgets = Array.CreateInstance(widgetType, 1);
        widgets.SetValue(ExportedCSharp.CreateEntity(widgetType, ("active", true)), 0);
        var printed = ExportedCSharp.CreateEntity(binType, ("active", false), ("widgets", widgets));
        var result = binType.GetMethod("Spawn", PrintedMembers.Any)!.Invoke(printed, null)!;
        await Assert.That((bool)result.GetType().GetProperty("IsSuccess", PrintedMembers.Any)!.GetValue(result)!).IsTrue();
        var printedWidgets = binType.GetProperty("Widgets", PrintedMembers.Any)!.GetValue(printed) as System.Collections.ICollection;
        await Assert.That(printedWidgets).IsNotNull();
        await Assert.That(printedWidgets!.Count).IsEqualTo(2);
        var activeCount = 0;
        foreach (var item in printedWidgets)
            if ((bool)widgetType.GetProperty("Active", PrintedMembers.Any)!.GetValue(item)!)
                activeCount++;
        await Assert.That(activeCount).IsEqualTo(2);
    }

    [Test]
    public async Task ActionShortCircuitAnd_SimulateAndGeneratedCSharp_Agree() {
        var (domain, analysis) = Evolve(BinDsl);
        var exec = ModuleExec.For(domain);
        await Assert.That(analysis.HasErrors).IsFalse();
        Entity E(string n) => domain.Types.OfType<Entity>().First(e => e.Name == n);
        var bin = exec.Create("Bin", new Dictionary<string, object?> { ["Active"] = false, ["N"] = 0L }).Instance!;
        var active = exec.Create("Widget", new Dictionary<string, object?> { ["Active"] = true }).Instance!;
        exec.Link("widgets", bin, active);
        var sim = exec.Invoke(bin, "Guarded");
        await Assert.That(sim.Succeeded).IsTrue();
        await Assert.That(Convert.ToInt64(bin.Bag["N"])).IsEqualTo(0L);

        var lowered = new EffectLoweringPass(E("Bin"), new LoweringContext(
            new ThisReference(),
            Analysis: analysis,
            Domain: domain, ActionResultType: new NamedTypeReference("DomainResult"))).LowerActionBody(E("Bin").Actions.First(a => a.Name == "Guarded").Effects);
        var ifs = Flatten(lowered!).OfType<IfStatement>().First(s =>
            Flatten(s.ThenBranch).OfType<ForEachLoop>().Any());
        await Assert.That(Flatten(ifs.Condition).OfType<ForEachLoop>().Any()).IsFalse();

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis)));
        var widgetType = asm.GetType("Widget")!;
        var binType = asm.GetType("Bin")!;
        var widgets = Array.CreateInstance(widgetType, 1);
        widgets.SetValue(ExportedCSharp.CreateEntity(widgetType, ("active", true)), 0);
        var printed = ExportedCSharp.CreateEntity(binType, ("active", false), ("n", 0L), ("widgets", widgets));
        var result = binType.GetMethod("Guarded", PrintedMembers.Any)!.Invoke(printed, null)!;
        await Assert.That((bool)result.GetType().GetProperty("IsSuccess", PrintedMembers.Any)!.GetValue(result)!).IsTrue();
        await Assert.That(Convert.ToInt64(binType.GetProperty("N", PrintedMembers.Any)!.GetValue(printed))).IsEqualTo(0L);
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
        var exec = ModuleExec.For(domain);
        await Assert.That(analysis.HasErrors).IsFalse();
        var bin = exec.Create("Bin", new Dictionary<string, object?> { ["N"] = 0L }).Instance!;
        var active = exec.Create("Widget", new Dictionary<string, object?> { ["Active"] = true, ["Flag"] = false }).Instance!;
        var idle = exec.Create("Widget", new Dictionary<string, object?> { ["Active"] = false, ["Flag"] = false }).Instance!;
        exec.Link("widgets", bin, active);
        exec.Link("widgets", bin, idle);
        var sim = exec.Invoke(bin, "Go");
        await Assert.That(sim.Succeeded).IsTrue();
        await Assert.That(Convert.ToInt64(bin.Bag["N"])).IsEqualTo(1L);
        await Assert.That((bool)active.Bag["Flag"]!).IsTrue();
        await Assert.That((bool)idle.Bag["Flag"]!).IsTrue();

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis)));
        var widgetType = asm.GetType("Widget")!;
        var binType = asm.GetType("Bin")!;
        var printedWidgets = Array.CreateInstance(widgetType, 2);
        printedWidgets.SetValue(ExportedCSharp.CreateEntity(widgetType, ("active", true), ("flag", false)), 0);
        printedWidgets.SetValue(ExportedCSharp.CreateEntity(widgetType, ("active", false), ("flag", false)), 1);
        var printed = ExportedCSharp.CreateEntity(binType, ("n", 0L), ("widgets", printedWidgets));
        var result = binType.GetMethod("Go", PrintedMembers.Any)!.Invoke(printed, null)!;
        await Assert.That((bool)result.GetType().GetProperty("IsSuccess", PrintedMembers.Any)!.GetValue(result)!).IsTrue();
        await Assert.That(Convert.ToInt64(binType.GetProperty("N", PrintedMembers.Any)!.GetValue(printed))).IsEqualTo(1L);
        var after = (System.Collections.ICollection)binType.GetProperty("Widgets", PrintedMembers.Any)!.GetValue(printed)!;
        foreach (var item in after)
            await Assert.That((bool)widgetType.GetProperty("Flag", PrintedMembers.Any)!.GetValue(item)!).IsTrue();
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
        var exec = ModuleExec.For(domain);
        await Assert.That(analysis.HasErrors).IsFalse();
        var bin = exec.Create("Bin", new Dictionary<string, object?>()).Instance!;
        var active = exec.Create("Widget", new Dictionary<string, object?> { ["Active"] = true }).Instance!;
        exec.Link("widgets", bin, active);
        var sim = exec.Invoke(bin, "Spawn");
        await Assert.That(sim.Succeeded).IsTrue();
        await Assert.That(sim.Instance).IsNotNull();
        var simActive = (bool)sim.Instance!.Bag["Active"]!;

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis)));
        var widgetType = asm.GetType("Widget")!;
        var binType = asm.GetType("Bin")!;
        var widgets = Array.CreateInstance(widgetType, 1);
        widgets.SetValue(ExportedCSharp.CreateEntity(widgetType, ("active", true)), 0);
        var printed = ExportedCSharp.CreateEntity(binType, ("widgets", widgets));
        var result = binType.GetMethod("Spawn", PrintedMembers.Any)!.Invoke(printed, null)!;
        await Assert.That((bool)result.GetType().GetProperty("IsSuccess", PrintedMembers.Any)!.GetValue(result)!).IsTrue();
        var printedWidget = result.GetType().GetProperty("Value", PrintedMembers.Any)!.GetValue(result)!;
        await Assert.That((bool)widgetType.GetProperty("Active", PrintedMembers.Any)!.GetValue(printedWidget)!).IsEqualTo(simActive);
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
              team: Team optional
              P: policy { team Active == any workers where Active }
            }
            """;
        var (domain, analysis) = Evolve(poly);
        var exec = ModuleExec.For(domain);
        await Assert.That(analysis.HasErrors).IsFalse();
        Entity E(string n) => domain.Types.OfType<Entity>().First(e => e.Name == n);
        ModuleInstance SimDept(bool teamActive, bool workerActive) {
            var worker = exec.Create("Worker", new Dictionary<string, object?> { ["Active"] = workerActive }).Instance!;
            var team = exec.Create("Team", new Dictionary<string, object?> { ["Active"] = teamActive }).Instance!;
            var dept = exec.Create("Dept", new Dictionary<string, object?>()).Instance!;
            exec.Link("workers", team, worker);
            exec.Link("team", dept, team);
            return dept;
        }
        var policy = E("Dept").Policies.First(p => p.Name == "P");
        var simTrue = exec.EvaluatePolicy(SimDept(true, true), policy.Name);
        var simFalse = exec.EvaluatePolicy(SimDept(true, false), policy.Name);
        await Assert.That(simTrue).IsTrue();
        await Assert.That(simFalse).IsFalse();

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis)));
        var workerType = asm.GetType("Worker")!;
        var teamType = asm.GetType("Team")!;
        var deptType = asm.GetType("Dept")!;
        object PrintDept(bool teamActive, bool workerActive) {
            var workers = Array.CreateInstance(workerType, 1);
            workers.SetValue(ExportedCSharp.CreateEntity(workerType, ("active", workerActive)), 0);
            var team = ExportedCSharp.CreateEntity(teamType, ("active", teamActive), ("workers", workers));
            return ExportedCSharp.CreateEntity(deptType, ("team", team));
        }
        bool PrintedP(object dept) => (bool)deptType.GetMethod("P", PrintedMembers.Any)!.Invoke(dept, null)!;
        await Assert.That(PrintedP(PrintDept(true, true))).IsEqualTo(simTrue);
        await Assert.That(PrintedP(PrintDept(true, false))).IsEqualTo(simFalse);

        var unlinked = exec.Create("Dept", new Dictionary<string, object?>()).Instance!;
        await Assert.That(exec.EvaluatePolicy(unlinked, (policy).Name)).IsFalse();
        var printedUnlinked = ExportedCSharp.CreateEntity(deptType);
        await Assert.That(PrintedP(printedUnlinked)).IsFalse();
    }

    [Test]
    public async Task PathPrefixHop_QuantifierNavigationNameCollision_SimulateAndGeneratedCSharp_Agree() {
        var poly = """
            domain Shop
            WorkerState: enum { Idle, Busy }
            TempState: enum { Busy, Idle }
            TeamState: enum { Done, Busy }
            Worker: entity { State: WorkerState }
            Temp: entity { State: TempState }
            Team: entity {
              Active: Boolean
              State: TeamState
              workers: many Worker
            }
            Dept: entity {
              team: Team optional
              workers: many Temp
              P: policy { team Active == any workers where State == "Busy" }
            }
            """;
        var (domain, analysis) = Evolve(poly);
        var exec = ModuleExec.For(domain);
        await Assert.That(analysis.HasErrors).IsFalse();
        Entity E(string n) => domain.Types.OfType<Entity>().First(e => e.Name == n);
        ModuleInstance SimDept(bool teamActive, string workerState) {
            var worker = exec.Create("Worker", new Dictionary<string, object?> { ["State"] = workerState }).Instance!;
            var team = exec.Create("Team", new Dictionary<string, object?> { ["Active"] = teamActive, ["State"] = "Done" }).Instance!;
            var dept = exec.Create("Dept", new Dictionary<string, object?>()).Instance!;
            exec.Link("workers", team, worker);
            exec.Link("team", dept, team);
            return dept;
        }
        var policy = E("Dept").Policies.First(p => p.Name == "P");
        var simTrue = exec.EvaluatePolicy(SimDept(true, "Busy"), policy.Name);
        var simFalse = exec.EvaluatePolicy(SimDept(true, "Idle"), policy.Name);
        await Assert.That(simTrue).IsTrue();
        await Assert.That(simFalse).IsFalse();

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis)));
        var workerType = asm.GetType("Worker")!;
        var teamType = asm.GetType("Team")!;
        var deptType = asm.GetType("Dept")!;
        var workerStateType = asm.GetType("WorkerState")!;
        var teamStateType = asm.GetType("TeamState")!;
        object PrintDept(bool teamActive, string workerState) {
            var workers = Array.CreateInstance(workerType, 1);
            workers.SetValue(ExportedCSharp.CreateEntity(workerType,
                ("state", Enum.Parse(workerStateType, workerState))), 0);
            var team = ExportedCSharp.CreateEntity(teamType,
                ("active", teamActive),
                ("state", Enum.Parse(teamStateType, "Done")),
                ("workers", workers));
            return ExportedCSharp.CreateEntity(deptType, ("team", team));
        }
        bool PrintedP(object dept) => (bool)deptType.GetMethod("P", PrintedMembers.Any)!.Invoke(dept, null)!;
        await Assert.That(PrintedP(PrintDept(true, "Busy"))).IsEqualTo(simTrue);
        await Assert.That(PrintedP(PrintDept(true, "Idle"))).IsEqualTo(simFalse);
    }

    [Test]
    public async Task TwoHopPathPrefix_QuantifierNavigationNameCollision_SimulateAndGeneratedCSharp_Agree() {
        var poly = """
            domain Shop
            WorkerState: enum { Idle, Busy }
            TempState: enum { Busy, Idle }
            TeamState: enum { Done, Busy }
            Worker: entity { State: WorkerState }
            Temp: entity { State: TempState }
            Team: entity {
              Active: Boolean
              State: TeamState
              workers: many Worker
            }
            Org: entity {
              team: Team optional
              workers: many Temp
            }
            Dept: entity {
              org: Org optional
              workers: many Temp
              P: policy { org team Active == any workers where State == "Busy" }
            }
            """;
        var (domain, analysis) = Evolve(poly);
        var exec = ModuleExec.For(domain);
        await Assert.That(analysis.HasErrors).IsFalse();
        Entity E(string n) => domain.Types.OfType<Entity>().First(e => e.Name == n);
        ModuleInstance SimDept(string workerState) {
            var worker = exec.Create("Worker", new Dictionary<string, object?> { ["State"] = workerState }).Instance!;
            var team = exec.Create("Team", new Dictionary<string, object?> { ["Active"] = true, ["State"] = "Done" }).Instance!;
            var org = exec.Create("Org", new Dictionary<string, object?>()).Instance!;
            var dept = exec.Create("Dept", new Dictionary<string, object?>()).Instance!;
            exec.Link("workers", team, worker);
            exec.Link("team", org, team);
            exec.Link("org", dept, org);
            return dept;
        }
        var policy = E("Dept").Policies.First(p => p.Name == "P");
        var simTrue = exec.EvaluatePolicy(SimDept("Busy"), policy.Name);
        var simFalse = exec.EvaluatePolicy(SimDept("Idle"), policy.Name);
        await Assert.That(simTrue).IsTrue();
        await Assert.That(simFalse).IsFalse();

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis)));
        var workerType = asm.GetType("Worker")!;
        var teamType = asm.GetType("Team")!;
        var orgType = asm.GetType("Org")!;
        var deptType = asm.GetType("Dept")!;
        var workerStateType = asm.GetType("WorkerState")!;
        var teamStateType = asm.GetType("TeamState")!;
        object PrintDept(string workerState) {
            var workers = Array.CreateInstance(workerType, 1);
            workers.SetValue(ExportedCSharp.CreateEntity(workerType,
                ("state", Enum.Parse(workerStateType, workerState))), 0);
            var team = ExportedCSharp.CreateEntity(teamType,
                ("active", true),
                ("state", Enum.Parse(teamStateType, "Done")),
                ("workers", workers));
            var org = ExportedCSharp.CreateEntity(orgType, ("team", team));
            return ExportedCSharp.CreateEntity(deptType, ("org", org));
        }
        bool PrintedP(object dept) => (bool)deptType.GetMethod("P", PrintedMembers.Any)!.Invoke(dept, null)!;
        await Assert.That(PrintedP(PrintDept("Busy"))).IsEqualTo(simTrue);
        await Assert.That(PrintedP(PrintDept("Idle"))).IsEqualTo(simFalse);

        var unlinked = exec.Create("Dept", new Dictionary<string, object?>()).Instance!;
        await Assert.That(exec.EvaluatePolicy(unlinked, (policy).Name)).IsFalse();
        await Assert.That(PrintedP(ExportedCSharp.CreateEntity(deptType))).IsFalse();
    }

    [Test]
    public async Task PathPrefixHop_EnumCompareOnTarget_UsesTargetEnum_SimulateAndGeneratedCSharp_Agree() {
        var poly = """
            domain Shop
            TeamState: enum { Done, Busy }
            DeptState: enum { Open, Done }
            Team: entity { State: TeamState }
            Dept: entity {
              State: DeptState
              team: Team optional
              P: policy { team State == "Done" }
            }
            """;
        var (domain, analysis) = Evolve(poly);
        var exec = ModuleExec.For(domain);
        await Assert.That(analysis.HasErrors).IsFalse();
        Entity E(string n) => domain.Types.OfType<Entity>().First(e => e.Name == n);
        ModuleInstance SimDept(string teamState) {
            var team = exec.Create("Team", new Dictionary<string, object?> { ["State"] = teamState }).Instance!;
            var dept = exec.Create("Dept", new Dictionary<string, object?> { ["State"] = "Open" }).Instance!;
            exec.Link("team", dept, team);
            return dept;
        }
        var policy = E("Dept").Policies.First(p => p.Name == "P");
        var simTrue = exec.EvaluatePolicy(SimDept("Done"), policy.Name);
        var simFalse = exec.EvaluatePolicy(SimDept("Busy"), policy.Name);
        await Assert.That(simTrue).IsTrue();
        await Assert.That(simFalse).IsFalse();

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis)));
        var teamType = asm.GetType("Team")!;
        var deptType = asm.GetType("Dept")!;
        var teamStateType = asm.GetType("TeamState")!;
        var deptStateType = asm.GetType("DeptState")!;
        object PrintDept(string teamState) {
            var team = ExportedCSharp.CreateEntity(teamType,
                ("state", Enum.Parse(teamStateType, teamState)));
            return ExportedCSharp.CreateEntity(deptType,
                ("state", Enum.Parse(deptStateType, "Open")),
                ("team", team));
        }
        bool PrintedP(object dept) => (bool)deptType.GetMethod("P", PrintedMembers.Any)!.Invoke(dept, null)!;
        await Assert.That(PrintedP(PrintDept("Done"))).IsEqualTo(simTrue);
        await Assert.That(PrintedP(PrintDept("Busy"))).IsEqualTo(simFalse);
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
        var exec = ModuleExec.For(domain);
        await Assert.That(analysis.HasErrors).IsFalse();
        Entity E(string n) => domain.Types.OfType<Entity>().First(e => e.Name == n);
        var due = new DateOnly(2026, 1, 1);
        ModuleInstance SimTicket(bool openLine) {
            var ticket = exec.Create("Ticket", new Dictionary<string, object?> { ["Due"] = due }).Instance!;
            if (openLine) {
                var line = exec.Create("Line", new Dictionary<string, object?> { ["Open"] = true }).Instance!;
                exec.Link("lines", ticket, line);
            }
            return ticket;
        }
        var policy = E("Ticket").Policies.First(p => p.Name == "P");
        var simOpen = exec.EvaluatePolicy(SimTicket(true), policy.Name);
        var simClosed = exec.EvaluatePolicy(SimTicket(false), policy.Name);
        await Assert.That(simOpen).IsTrue();
        await Assert.That(simClosed).IsFalse();

        var cs = new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis));
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
        bool PrintedP(object ticket) => (bool)ticketType.GetMethod("P", PrintedMembers.Any)!.Invoke(ticket, null)!;
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
        var exec = ModuleExec.For(domain);
        await Assert.That(analysis.HasErrors).IsFalse();
        var bin = exec.Create("Bin", new Dictionary<string, object?> { ["N"] = 0L }).Instance!;
        var widget = exec.Create("Widget", new Dictionary<string, object?> { ["Level"] = 0L }).Instance!;
        exec.Link("widgets", bin, widget);
        var simHigh = exec.Invoke(bin, "Close", new Dictionary<string, object?> { ["Level"] = 10L });
        await Assert.That(simHigh.Succeeded).IsTrue();
        await Assert.That(Convert.ToInt64(bin.Bag["N"])).IsEqualTo(1L);

        var binLow = exec.Create("Bin", new Dictionary<string, object?> { ["N"] = 0L }).Instance!;
        var widgetLow = exec.Create("Widget", new Dictionary<string, object?> { ["Level"] = 0L }).Instance!;
        exec.Link("widgets", binLow, widgetLow);
        var simLow = exec.Invoke(binLow, "Close", new Dictionary<string, object?> { ["Level"] = 2L });
        await Assert.That(simLow.Succeeded).IsTrue();
        await Assert.That(Convert.ToInt64(binLow.Bag["N"])).IsEqualTo(0L);

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis)));
        var widgetType = asm.GetType("Widget")!;
        var binType = asm.GetType("Bin")!;
        object PrintBin() {
            var widgets = Array.CreateInstance(widgetType, 1);
            widgets.SetValue(ExportedCSharp.CreateEntity(widgetType, ("level", 0L)), 0);
            return ExportedCSharp.CreateEntity(binType, ("n", 0L), ("widgets", widgets));
        }
        var close = binType.GetMethod("Close", PrintedMembers.Any)!;
        var printedHigh = PrintBin();
        var highResult = close.Invoke(printedHigh, [10L])!;
        await Assert.That((bool)highResult.GetType().GetProperty("IsSuccess", PrintedMembers.Any)!.GetValue(highResult)!).IsTrue();
        await Assert.That(Convert.ToInt64(binType.GetProperty("N", PrintedMembers.Any)!.GetValue(printedHigh))).IsEqualTo(1L);
        var printedLow = PrintBin();
        var lowResult = close.Invoke(printedLow, [2L])!;
        await Assert.That((bool)lowResult.GetType().GetProperty("IsSuccess", PrintedMembers.Any)!.GetValue(lowResult)!).IsTrue();
        await Assert.That(Convert.ToInt64(binType.GetProperty("N", PrintedMembers.Any)!.GetValue(printedLow))).IsEqualTo(0L);
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
        var exec = ModuleExec.For(domain);
        await Assert.That(analysis.HasErrors).IsFalse();
        var openPart = exec.Create("Part", new Dictionary<string, object?> { ["Open"] = true }).Instance!;
        var closedPart = exec.Create("Part", new Dictionary<string, object?> { ["Open"] = false }).Instance!;
        var withOpen = exec.Create("Widget", new Dictionary<string, object?> { ["Flag"] = false }).Instance!;
        var withoutOpen = exec.Create("Widget", new Dictionary<string, object?> { ["Flag"] = false }).Instance!;
        var bin = exec.Create("Bin", new Dictionary<string, object?>()).Instance!;
        exec.Link("parts", withOpen, openPart);
        exec.Link("parts", withoutOpen, closedPart);
        exec.Link("widgets", bin, withOpen);
        exec.Link("widgets", bin, withoutOpen);
        var sim = exec.Invoke(bin, "Go");
        await Assert.That(sim.Succeeded).IsTrue();
        await Assert.That((bool)withOpen.Bag["Flag"]!).IsTrue();
        await Assert.That((bool)withoutOpen.Bag["Flag"]!).IsFalse();

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis)));
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
        var result = binType.GetMethod("Go", PrintedMembers.Any)!.Invoke(printed, null)!;
        await Assert.That((bool)result.GetType().GetProperty("IsSuccess", PrintedMembers.Any)!.GetValue(result)!).IsTrue();
        var after = (System.Collections.ICollection)binType.GetProperty("Widgets", PrintedMembers.Any)!.GetValue(printed)!;
        var flags = new List<bool>();
        foreach (var item in after)
            flags.Add((bool)widgetType.GetProperty("Flag", PrintedMembers.Any)!.GetValue(item)!);
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