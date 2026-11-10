using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// #405 slice 4, route keys: a declared <c>key(...)</c> reaches storage as the ordered route-key
/// segment names a route emitter consumes. Only a declared key produces one: a keyless entity (even
/// one with a <c>unique</c> property) produces none, as does a key naming a navigation.
/// </summary>
public sealed class EntityKeyRouteKeysTests {
    private static Property Text(string name) => new(name, new DomainTypeReference("Text"), []);

    private static StorageEntity Storage(Entity entity, string name) {
        var domain = ValidDomain.Create("Test", [entity]);
        return new StorageAnalyzer(domain, DomainModelAnalyzer.Analyze(domain))
            .Analyze().Entities.Single(e => e.Name == name);
    }

    [Test]
    public async Task KeyedEntity_RouteKeysAreCamelCasedInKeyOrder() {
        var order = new Entity("Order", [Text("Number"), Text("Region"), Text("Note")], [], [], []) { KeyProperties = ["Region", "Number"] };

        var routeKeys = Storage(order, "Order").RouteKeys;

        await Assert.That(routeKeys.Count).IsEqualTo(2);
        await Assert.That(routeKeys[0]).IsEqualTo("region");
        await Assert.That(routeKeys[1]).IsEqualTo("number");
    }

    [Test]
    public async Task KeyedEntity_RouteKeysAreDeterministic() {
        var order = new Entity("Order", [Text("Number"), Text("Region")], [], [], []) { KeyProperties = ["Number", "Region"] };

        await Assert.That(Storage(order, "Order").RouteKeys).IsEquivalentTo(Storage(order, "Order").RouteKeys);
    }

    [Test]
    public async Task KeylessEntity_ProducesNoRouteKeys() {
        await Assert.That(Storage(new Entity("Note", [Text("Body")], [], [], []), "Note").RouteKeys).IsEmpty();
    }

    [Test]
    public async Task UniqueButUndeclared_ProducesNoRouteKeys() {
        var unique = new Property("Isbn", new DomainTypeReference("Text"), [new UniqueConstraint()]);
        var book = new Entity("Book", [unique], [], [], []);

        await Assert.That(Storage(book, "Book").RouteKeys).IsEmpty();
    }

    [Test]
    public async Task KeyedEntity_DoesNotChangeLegacyKeyName() {
        var keyed = new Entity("Order", [Text("Number")], [], [], []) { KeyProperties = ["Number"] };
        var unkeyed = new Entity("Order", [Text("Number")], [], [], []);

        await Assert.That(Storage(keyed, "Order").KeyName).IsEqualTo(Storage(unkeyed, "Order").KeyName);
    }
}