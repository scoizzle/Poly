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
    public async Task DomainMutationIntentEngine_Apply_AddActorType_CreatesActorEntity() {
        var domain = DomainTestFactory.CreateDomain();
        var engine = new DomainMutationIntentEngine();

        _ = engine.Apply(domain, new AddActorTypeIntent("User"));

        var actor = domain.RequireActor("User");

        await Assert.That(actor is Actor).IsTrue();
        await Assert.That(domain.RequireEntity("User") is Actor).IsTrue();
    }

    [Test]
    public async Task DomainMutationIntent_Serialization_RoundTripsPolymorphicIntents() {
        DomainMutationIntent[] intents = [
            new SetDomainNameIntent("Orders"),
            new AddPrimitiveTypeIntent("money", TypeCategory.Numeric),
            new AddEntityTypeIntent("Customer", new DomainNodeReference("parent-id")),
            new AddActorTypeIntent("User", new DomainNodeReference("principal-id")),
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
        await Assert.That(hydrated!.Length).IsEqualTo(5);
        await Assert.That(hydrated[0] is SetDomainNameIntent).IsTrue();
        await Assert.That(hydrated[1] is AddPrimitiveTypeIntent).IsTrue();
        await Assert.That(hydrated[2] is AddEntityTypeIntent).IsTrue();
        await Assert.That(hydrated[3] is AddActorTypeIntent).IsTrue();
        await Assert.That(hydrated[4] is AddRelationshipIntent).IsTrue();
    }

    [Test]
    public async Task DomainMutationIntentEngine_Apply_SetActorSubjectProperty_SetsProperty() {
        var domain = DomainTestFactory.CreateDomain();
        var engine = new DomainMutationIntentEngine();

        _ = engine.Apply(domain, new AddPrimitiveTypeIntent("string", TypeCategory.Text));
        _ = engine.Apply(domain, new AddActorTypeIntent("User"));
        _ = engine.Apply(domain, new AddPropertyToEntityIntent("User", "ExternalId", "string"));
        _ = engine.Apply(domain, new SetActorSubjectPropertyIntent("User", "ExternalId"));

        var actor = domain.RequireActor("User");

        await Assert.That(actor.SubjectProperty?.Name).IsEqualTo("ExternalId");
        await Assert.That(actor.RoleClaimType).IsNull();
        await Assert.That(actor.ClaimMappings).IsEmpty();
    }

    [Test]
    public async Task DomainMutationIntentEngine_Apply_SetActorSubjectProperty_NullClears() {
        var domain = DomainTestFactory.CreateDomain();
        var engine = new DomainMutationIntentEngine();

        _ = engine.Apply(domain, new AddPrimitiveTypeIntent("string", TypeCategory.Text));
        _ = engine.Apply(domain, new AddActorTypeIntent("User"));
        _ = engine.Apply(domain, new AddPropertyToEntityIntent("User", "ExternalId", "string"));
        _ = engine.Apply(domain, new SetActorSubjectPropertyIntent("User", "ExternalId"));
        _ = engine.Apply(domain, new SetActorSubjectPropertyIntent("User", null));

        await Assert.That(domain.RequireActor("User").SubjectProperty).IsNull();
    }

    [Test]
    public async Task DomainMutationIntentEngine_Apply_SetActorRoleClaimType_SetsValue() {
        var domain = DomainTestFactory.CreateDomain();
        var engine = new DomainMutationIntentEngine();

        _ = engine.Apply(domain, new AddActorTypeIntent("User"));
        _ = engine.Apply(domain, new SetActorRoleClaimTypeIntent("User", "roles"));

        await Assert.That(domain.RequireActor("User").RoleClaimType).IsEqualTo("roles");
    }

    [Test]
    public async Task DomainMutationIntentEngine_Apply_AddActorClaimMapping_AddsMapping() {
        var domain = DomainTestFactory.CreateDomain();
        var engine = new DomainMutationIntentEngine();

        _ = engine.Apply(domain, new AddPrimitiveTypeIntent("string", TypeCategory.Text));
        _ = engine.Apply(domain, new AddActorTypeIntent("User"));
        _ = engine.Apply(domain, new AddPropertyToEntityIntent("User", "Email", "string"));
        _ = engine.Apply(domain, new AddActorClaimMappingIntent("User", "email", "Email"));

        var actor = domain.RequireActor("User");

        await Assert.That(actor.ClaimMappings.Count).IsEqualTo(1);
        await Assert.That(actor.ClaimMappings.First().ClaimType).IsEqualTo("email");
        await Assert.That(actor.ClaimMappings.First().Property.Name).IsEqualTo("Email");
    }

    [Test]
    public async Task DomainMutationIntentEngine_Apply_RemoveActorClaimMapping_RemovesMapping() {
        var domain = DomainTestFactory.CreateDomain();
        var engine = new DomainMutationIntentEngine();

        _ = engine.Apply(domain, new AddPrimitiveTypeIntent("string", TypeCategory.Text));
        _ = engine.Apply(domain, new AddActorTypeIntent("User"));
        _ = engine.Apply(domain, new AddPropertyToEntityIntent("User", "Email", "string"));
        _ = engine.Apply(domain, new AddActorClaimMappingIntent("User", "email", "Email"));
        _ = engine.Apply(domain, new RemoveActorClaimMappingIntent("User", "email"));

        await Assert.That(domain.RequireActor("User").ClaimMappings).IsEmpty();
    }

    [Test]
    public async Task Actor_IdentityProfile_IsComputedSnapshot() {
        var domain = DomainTestFactory.CreateDomain();
        var engine = new DomainMutationIntentEngine();

        _ = engine.Apply(domain, new AddPrimitiveTypeIntent("string", TypeCategory.Text));
        _ = engine.Apply(domain, new AddActorTypeIntent("User"));
        _ = engine.Apply(domain, new AddPropertyToEntityIntent("User", "ExternalId", "string"));
        _ = engine.Apply(domain, new AddPropertyToEntityIntent("User", "Email", "string"));
        _ = engine.Apply(domain, new SetActorSubjectPropertyIntent("User", "ExternalId"));
        _ = engine.Apply(domain, new SetActorRoleClaimTypeIntent("User", "roles"));
        _ = engine.Apply(domain, new AddActorClaimMappingIntent("User", "email", "Email"));

        var actor = domain.RequireActor("User");
        var snapshot = actor.IdentityProfile;

        await Assert.That(snapshot.SubjectProperty?.Name).IsEqualTo("ExternalId");
        await Assert.That(snapshot.RoleClaimType).IsEqualTo("roles");
        await Assert.That(snapshot.ClaimMappings.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Actor_SetSubjectProperty_Command_Rollback_RestoresPreviousValue() {
        var domain = DomainTestFactory.CreateDomain();
        var engine = new DomainMutationIntentEngine();

        _ = engine.Apply(domain, new AddPrimitiveTypeIntent("string", TypeCategory.Text));
        _ = engine.Apply(domain, new AddActorTypeIntent("User"));
        _ = engine.Apply(domain, new AddPropertyToEntityIntent("User", "ExternalId", "string"));
        _ = engine.Apply(domain, new SetActorSubjectPropertyIntent("User", "ExternalId"));

        var actor = domain.RequireActor("User");
        var subjectProp = actor.SubjectProperty;

        // clear and verify
        actor.SetSubjectProperty(null);
        await Assert.That(actor.SubjectProperty).IsNull();

        // restore
        actor.SetSubjectProperty(subjectProp);
        await Assert.That(actor.SubjectProperty?.Name).IsEqualTo("ExternalId");
    }

    [Test]
    public async Task DomainMutationIntent_Serialization_RoundTrips_ActorIdentityIntents() {
        DomainMutationIntent[] intents = [
            new SetActorSubjectPropertyIntent("User", "ExternalId"),
            new SetActorRoleClaimTypeIntent("User", "roles"),
            new AddActorClaimMappingIntent("User", "email", "Email"),
            new RemoveActorClaimMappingIntent("User", "email")
        ];

        var json = JsonSerializer.Serialize(intents);
        var hydrated = JsonSerializer.Deserialize<DomainMutationIntent[]>(json);

        await Assert.That(hydrated is not null).IsTrue();
        await Assert.That(hydrated!.Length).IsEqualTo(4);
        await Assert.That(hydrated[0] is SetActorSubjectPropertyIntent).IsTrue();
        await Assert.That(hydrated[1] is SetActorRoleClaimTypeIntent).IsTrue();
        await Assert.That(hydrated[2] is AddActorClaimMappingIntent).IsTrue();
        await Assert.That(hydrated[3] is RemoveActorClaimMappingIntent).IsTrue();

        var addMapping = (AddActorClaimMappingIntent)hydrated[2];
        await Assert.That(addMapping.ActorName).IsEqualTo("User");
        await Assert.That(addMapping.ClaimType).IsEqualTo("email");
        await Assert.That(addMapping.PropertyName).IsEqualTo("Email");
    }
}