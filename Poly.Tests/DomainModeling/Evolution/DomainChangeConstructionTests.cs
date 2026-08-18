using Poly.DomainModeling;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Evolution;

/// <summary>
/// Smoke tests that the first MVP DomainChange record types can be constructed.
/// These are pure data carriers — applicator behavior is tested in subsequent tasks.
/// </summary>
public class DomainChangeConstructionTests {
    [Test]
    public async Task AddEntityChange_CanBeConstructed() {
        var change = new AddEntityChange("Order", []);

        await Assert.That(change.Name).IsEqualTo("Order");
        await Assert.That(change.InitialProperties).IsEmpty();
        await Assert.That(change).IsAssignableTo<DomainChange>();
    }

    [Test]
    public async Task RemoveEntityChange_CanBeConstructed() {
        var change = new RemoveEntityChange("Customer");

        await Assert.That(change.Name).IsEqualTo("Customer");
        await Assert.That(change).IsAssignableTo<DomainChange>();
    }

    [Test]
    public async Task AddPropertyToEntityChange_CanBeConstructed() {
        var prop = new Property("Status", new DomainTypeReference("Text"), []);
        var change = new AddPropertyToEntityChange("Order", prop);

        await Assert.That(change.EntityName).IsEqualTo("Order");
        await Assert.That(change.Property.Name).IsEqualTo("Status");
        await Assert.That(change).IsAssignableTo<DomainChange>();
    }

    [Test]
    public async Task RemovePropertyFromEntityChange_CanBeConstructed() {
        var change = new RemovePropertyFromEntityChange("Order", "Status");

        await Assert.That(change.EntityName).IsEqualTo("Order");
        await Assert.That(change.PropertyName).IsEqualTo("Status");
        await Assert.That(change).IsAssignableTo<DomainChange>();
    }
}