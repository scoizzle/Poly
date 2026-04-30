using System.Text.Json;

using Poly.Data.Modeling;
using Poly.Data.Modeling.TypeSystem;
using Poly.Introspection;

namespace Poly.Tests.Data.Modeling;

public class DomainMutationIntentEngineTests {
    [Test]
    public async Task DomainMutationIntentEngine_Apply_WithCoreIntents_UpdatesDomain() {
        var domain = DomainTestFactory.CreateDomain();
        var engine = new DomainMutationIntentEngine();

        _ = engine.Apply(domain, [
            new SetDomainNameIntent("Support Domain"),
            new AddPrimitiveTypeIntent("string", TypeCategory.Text),
            new AddEntityTypeIntent("Customer"),
            new AddEntityTypeIntent("SupportCase")
        ]);

        var customer = domain.RequireEntity("Customer");
        var supportCase = domain.RequireEntity("SupportCase");

        _ = engine.Apply(domain, new AddRelationshipIntent(
            "CustomerCases",
            DomainNodeReference.From(customer),
            DomainNodeReference.From(supportCase),
            RelationshipCardinality.OneToMany,
            false));

        await Assert.That(domain.Name).IsEqualTo("Support Domain");
        await Assert.That(domain.RequirePrimitive("string").Category).IsEqualTo(TypeCategory.Text);
        await Assert.That(domain.FindRelationship("CustomerCases") is not null).IsTrue();
    }

    [Test]
    public async Task DomainMutationIntentEngine_Apply_AddEntityWithParentReference_AssignsParent() {
        var domain = DomainTestFactory.CreateDomain();
        var engine = new DomainMutationIntentEngine();

        _ = engine.Apply(domain, new AddEntityTypeIntent("Parent"));

        var parent = domain.RequireEntity("Parent");

        _ = engine.Apply(domain, new AddEntityTypeIntent("Child", DomainNodeReference.From(parent)));

        var child = domain.RequireEntity("Child");

        await Assert.That(ReferenceEquals(child.ParentEntity, parent)).IsTrue();
    }

    [Test]
    public async Task DomainMutationIntent_Serialization_RoundTripsPolymorphicIntents() {
        DomainMutationIntent[] intents = [
            new SetDomainNameIntent("Orders"),
            new AddPrimitiveTypeIntent("money", TypeCategory.Numeric),
            new AddEntityTypeIntent("Customer", new DomainNodeReference("parent-id")),
            new AddRelationshipIntent(
                "CustomerOrders",
                new DomainNodeReference("source-id"),
                new DomainNodeReference("target-id"),
                RelationshipCardinality.OneToMany,
                true)
        ];

        var json = JsonSerializer.Serialize(intents);
        var hydrated = JsonSerializer.Deserialize<DomainMutationIntent[]>(json);

        await Assert.That(hydrated is not null).IsTrue();
        await Assert.That(hydrated!.Length).IsEqualTo(4);
        await Assert.That(hydrated[0] is SetDomainNameIntent).IsTrue();
        await Assert.That(hydrated[1] is AddPrimitiveTypeIntent).IsTrue();
        await Assert.That(hydrated[2] is AddEntityTypeIntent).IsTrue();
        await Assert.That(hydrated[3] is AddRelationshipIntent).IsTrue();
    }
}