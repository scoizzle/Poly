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
        Entity E(string name) => domain.Types.OfType<Entity>().First(e => e.Name == name);
        var store = new DomainInstanceStore();
        var bin = DomainEntityInstance.Create(E("Bin"), domain: domain);
        var widget = DomainEntityInstance.Create(E("Widget"), domain: domain);
        store.Add(bin);
        store.Add(widget);
        store.Link("widgets", bin, widget);

        await Assert.That(bin.InvokeAction("Go").Succeeded).IsTrue();
        await Assert.That(widget.GetProperty<long>("Count")).IsEqualTo(1L);

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis)));
        var printedBin = ExportedCSharp.CreateEntity(asm, "Bin");
        var printedWidget = ExportedCSharp.CreateEntity(asm, "Widget");
        var widgets = printedBin.GetType()
            .GetField("_widgets", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(printedBin)!;
        widgets.GetType().GetMethod("Add")!.Invoke(widgets, [printedWidget]);

        var result = printedBin.GetType().GetMethod("Go")!.Invoke(printedBin, null)!;
        await Assert.That((bool)result.GetType().GetProperty("IsSuccess")!.GetValue(result)!).IsTrue();
        await Assert.That((long)printedWidget.GetType().GetProperty("Count")!.GetValue(printedWidget)!)
            .IsEqualTo(1L);
    }

    [Test]
    public async Task Unlink_RemovesSubscriberFromRegistry_LaterNotifyDoesNotFire() {
        var poly = """
            domain Workshop
            Widget: entity {
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
        Entity E(string name) => domain.Types.OfType<Entity>().First(e => e.Name == name);
        var store = new DomainInstanceStore();
        var bin = DomainEntityInstance.Create(
            E("Bin"), new Dictionary<string, object?> { ["Fires"] = 0L }, domain);
        var kept = DomainEntityInstance.Create(E("Widget"), domain: domain);
        var dropped = DomainEntityInstance.Create(E("Widget"), domain: domain);
        store.Add(bin);
        store.Add(kept);
        store.Add(dropped);
        store.Link("widgets", bin, kept);
        store.Link("widgets", bin, dropped);

        await Assert.That(kept.InvokeAction("Go").Succeeded).IsTrue();
        await Assert.That(bin.GetProperty<object>("Fires")).IsEqualTo(1L);

        store.Unlink("widgets", bin, dropped);
        IDictionary<string, object?> droppedBag = dropped;
        await Assert.That(droppedBag["_binActiveSubscribers"]).IsNull();
        IDictionary<string, object?> keptBag = kept;
        var keptRegistry = keptBag["_binActiveSubscribers"] as System.Collections.IList;
        await Assert.That(keptRegistry).IsNotNull();
        await Assert.That(keptRegistry!.Count).IsEqualTo(1);

        await Assert.That(dropped.InvokeAction("Go").Succeeded).IsTrue();
        await Assert.That(bin.GetProperty<object>("Fires")).IsEqualTo(1L);
    }
}
