using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// #405 slice 3, composite index: a declared <c>key(...)</c> reaches storage as one unique index
/// over the key's columns, in key order. Keyless entities produce nothing. A key naming a
/// navigation yields no index (its foreign-key shape is an open decision).
/// </summary>
public sealed class EntityKeyStorageIndexTests {
    private static Property Text(string name) => new(name, new DomainTypeReference("Text"), []);

    private static StorageEntity Storage(Entity entity, string name) {
        var domain = ValidDomain.Create("Test", [entity]);
        return new StorageAnalyzer(domain, DomainModelAnalyzer.Analyze(domain))
            .Analyze().Entities.Single(e => e.Name == name);
    }

    [Test]
    public async Task KeyedEntity_ProducesUniqueCompositeIndexInKeyOrder() {
        var order = new Entity("Order", [Text("Number"), Text("Region"), Text("Note")], [], [], []) { KeyProperties = ["Region", "Number"] };

        var index = Storage(order, "Order").KeyIndex;

        await Assert.That(index).IsNotNull();
        await Assert.That(index!.IsUnique).IsTrue();
        await Assert.That(index.Columns.Select(c => c.Name)).IsEquivalentTo(new[] { "Region", "Number" });
        await Assert.That(index.Columns.Select(c => c.Name).First()).IsEqualTo("Region");
    }

    [Test]
    public async Task KeyedEntity_IndexIsDeterministic() {
        var order = new Entity("Order", [Text("Number"), Text("Region")], [], [], []) { KeyProperties = ["Number", "Region"] };

        var first = Storage(order, "Order").KeyIndex!.Columns.Select(c => c.ColumnName).ToList();
        var second = Storage(order, "Order").KeyIndex!.Columns.Select(c => c.ColumnName).ToList();

        await Assert.That(first).IsEquivalentTo(second);
        await Assert.That(first.Count).IsEqualTo(2);
    }

    [Test]
    public async Task KeylessEntity_ProducesNoIndex() {
        var note = new Entity("Note", [Text("Body")], [], [], []);

        await Assert.That(Storage(note, "Note").KeyIndex).IsNull();
    }

    [Test]
    public async Task KeyedEntity_DoesNotChangeOtherStorageFacts() {
        var keyed = new Entity("Order", [Text("Number")], [], [], []) { KeyProperties = ["Number"] };
        var unkeyed = new Entity("Order", [Text("Number")], [], [], []);

        var a = Storage(keyed, "Order");
        var b = Storage(unkeyed, "Order");

        await Assert.That(a.TableName).IsEqualTo(b.TableName);
        await Assert.That(a.KeyName).IsEqualTo(b.KeyName);
        await Assert.That(a.ForeignKeys.Count).IsEqualTo(b.ForeignKeys.Count);
        await Assert.That(a.Columns.Select(c => c.ColumnName)).IsEquivalentTo(b.Columns.Select(c => c.ColumnName));
    }
}