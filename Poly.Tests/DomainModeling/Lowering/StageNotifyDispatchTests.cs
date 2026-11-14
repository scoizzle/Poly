using System.Reflection;

using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

public class StageNotifyDispatchTests {
    private const string Dsl = """
        domain Workshop
        Widget: entity {
          Count: Number default(0)
          NotifyAllSubscribers: action { assign Count to Count + 1 }
        }
        Bin: entity {
          widgets: many Widget
          Go: action { for widgets as w invoke w.NotifyAllSubscribers() }
        }
        """;

    [Test]
    public async Task ActionNamedNotifyAllSubscribers_IsNotTreatedAsStageNotify() {
        var (domain, analysis) = EvolvedDomain.FromDsl(Dsl);
        var exec = ModuleExec.For(domain);
        var bin = exec.Create("Bin", new Dictionary<string, object?>()).Instance!;
        var widget = exec.Create("Widget", new Dictionary<string, object?>()).Instance!;
        exec.Link("widgets", bin, widget);

        await Assert.That(exec.Invoke(bin, "Go").Succeeded).IsTrue();
        await Assert.That((long)widget.Bag["Count"]!).IsEqualTo(1L);

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis)));
        var printedBin = ExportedCSharp.CreateEntity(asm, "Bin");
        var printedWidget = ExportedCSharp.CreateEntity(asm, "Widget");
        var widgets = printedBin.GetType()
            .GetField("_widgets", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(printedBin)!;
        widgets.GetType().GetMethod("Add", PrintedMembers.Any)!.Invoke(widgets, [printedWidget]);

        var result = printedBin.GetType().GetMethod("Go", PrintedMembers.Any)!.Invoke(printedBin, null)!;
        await Assert.That((bool)result.GetType().GetProperty("IsSuccess", PrintedMembers.Any)!.GetValue(result)!).IsTrue();
        await Assert.That((long)printedWidget.GetType().GetProperty("Count", PrintedMembers.Any)!.GetValue(printedWidget)!)
            .IsEqualTo(1L);
    }

    [Test]
    public async Task Unlink_RemovesSubscriberFromRegistry_LaterNotifyDoesNotFire() {
        var poly = """
            domain Workshop
            Widget: entity {
              bin: Bin via widgets optional
              Draft: stage {
                Go: action { transition to Active }
              }
              Active: stage { }
            }
            Bin: entity {
              Fires: Number default(0)
              widgets: many Widget
              when widgets Active {
                assign Fires to Fires + 1
              }
            }
            """;
        var (domain, _) = EvolvedDomain.FromDsl(poly);
        var exec = ModuleExec.For(domain);
        var bin = exec.Create("Bin", new Dictionary<string, object?> { ["Fires"] = 0L }).Instance!;
        var kept = exec.Create("Widget", new Dictionary<string, object?>()).Instance!;
        var dropped = exec.Create("Widget", new Dictionary<string, object?>()).Instance!;
        exec.Link("widgets", bin, kept);
        exec.Link("widgets", bin, dropped);

        await Assert.That(exec.Invoke(kept, "Go").Succeeded).IsTrue();
        await Assert.That(bin.Bag["Fires"]).IsEqualTo(1L);

        exec.Unlink("widgets", bin, dropped);
        var droppedBag = dropped.Bag;
        await Assert.That(droppedBag.TryGetValue("_binWidgetsActiveSubscribers", out var droppedRegistry) ? droppedRegistry : null).IsNull();
        var keptBag = kept.Bag;
        var keptRegistry = keptBag["_binWidgetsActiveSubscribers"] as System.Collections.IList;
        await Assert.That(keptRegistry).IsNotNull();
        await Assert.That(keptRegistry!.Count).IsEqualTo(1);

        await Assert.That(exec.Invoke(dropped, "Go").Succeeded).IsTrue();
        await Assert.That(bin.Bag["Fires"]).IsEqualTo(1L);
    }

    [Test]
    public async Task Unlink_SiblingRelationship_KeepsOtherSubscriptionFiring() {
        var poly = """
            domain Workshop
            Widget: entity {
              binViaExtras: Bin via extras optional
              bin: Bin via widgets optional
              Draft: stage {
                Go: action { transition to Active }
              }
              Active: stage { }
            }
            Bin: entity {
              Fires: Number default(0)
              widgets: many Widget
              extras: many Widget
              when widgets Active {
                assign Fires to Fires + 1
              }
              when extras Active {
                assign Fires to Fires + 10
              }
            }
            """;
        var (domain, _) = EvolvedDomain.FromDsl(poly);
        var exec = ModuleExec.For(domain);
        var bin = exec.Create("Bin", new Dictionary<string, object?> { ["Fires"] = 0L }).Instance!;
        var first = exec.Create("Widget", new Dictionary<string, object?>()).Instance!;
        var second = exec.Create("Widget", new Dictionary<string, object?>()).Instance!;
        exec.Link("widgets", bin, first);
        exec.Link("extras", bin, first);
        exec.Link("widgets", bin, second);
        exec.Link("extras", bin, second);

        await Assert.That(exec.Invoke(first, "Go").Succeeded).IsTrue();
        await Assert.That(bin.Bag["Fires"]).IsEqualTo(11L);

        exec.Unlink("nope", bin, second);
        exec.Unlink("widgets", bin, second);
        var secondBag = second.Bag;
        await Assert.That(secondBag.TryGetValue("_binWidgetsActiveSubscribers", out var widgets) ? widgets : null).IsNull();
        var extras = secondBag["_binExtrasActiveSubscribers"] as System.Collections.IList;
        await Assert.That(extras).IsNotNull();
        await Assert.That(extras!.Count).IsEqualTo(1);

        await Assert.That(exec.Invoke(second, "Go").Succeeded).IsTrue();
        await Assert.That(bin.Bag["Fires"]).IsEqualTo(21L);
    }
}