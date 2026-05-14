using Poly.Data.Modeling;
using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.TypeSystem;
using Poly.DomainModeling.V2;
using Poly.DomainModeling.V2.Demos;
using Poly.Introspection;

namespace Poly.Tests.DomainModeling.V2;

public class DomainSessionTests {
    // ── DomainSessionStore ────────────────────────────────────────────────────

    [Test]
    public async Task Create_ReturnsDomainSession_WithCanonicalBuiltIns() {
        var store = new DomainSessionStore();
        var (sessionId, session) = store.Create("My Domain");

        await Assert.That(sessionId).IsNotEmpty();
        await Assert.That(session).IsNotNull();
        await Assert.That(session.Domain.Name).IsEqualTo("My Domain");
        await Assert.That(session.Domain.GetAvailablePrimitives().Any()).IsTrue();
    }

    [Test]
    public async Task Create_WithPreferredId_UsesProvidedId() {
        var store = new DomainSessionStore();
        var (sessionId, _) = store.Create("Test", "my-session");

        await Assert.That(sessionId).IsEqualTo("my-session");
    }

    [Test]
    public async Task Create_WithExistingPreferredId_ReturnsExistingSession() {
        var store = new DomainSessionStore();
        var (_, first) = store.Create("Domain A", "shared-id");
        var (_, second) = store.Create("Domain B", "shared-id");

        await Assert.That(ReferenceEquals(first, second)).IsTrue();
    }

    [Test]
    public async Task TryGet_WithKnownId_ReturnsSession() {
        var store = new DomainSessionStore();
        var (sessionId, created) = store.Create("Domain");

        var found = store.TryGet(sessionId, out var retrieved);

        await Assert.That(found).IsTrue();
        await Assert.That(ReferenceEquals(created, retrieved)).IsTrue();
    }

    [Test]
    public async Task TryGet_WithUnknownId_ReturnsFalse() {
        var store = new DomainSessionStore();
        var found = store.TryGet("nonexistent", out var session);

        await Assert.That(found).IsFalse();
        await Assert.That(session).IsNull();
    }

    [Test]
    public async Task Remove_WithKnownId_RemovesSession() {
        var store = new DomainSessionStore();
        var (sessionId, _) = store.Create("Domain");

        var removed = store.Remove(sessionId);
        var found = store.TryGet(sessionId, out _);

        await Assert.That(removed).IsTrue();
        await Assert.That(found).IsFalse();
    }

    [Test]
    public async Task ListSessionIds_ReturnsSortedIds() {
        var store = new DomainSessionStore();
        store.Create("A", "bbb");
        store.Create("B", "aaa");
        store.Create("C", "ccc");

        var ids = store.ListSessionIds().ToArray();

        await Assert.That(ids).IsEquivalentTo(new[] { "aaa", "bbb", "ccc" });
    }

    // ── DomainSession.Apply (single intent) ───────────────────────────────────

    [Test]
    public async Task Apply_AddEntityIntent_SucceedsAndIncrementsRevision() {
        var store = new DomainSessionStore();
        var (_, session) = store.Create("Test");
        var initialRevision = session.Revision;

        var result = session.Apply(new AddEntityTypeIntent("Customer"));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(session.Revision).IsEqualTo(initialRevision + 1);
        await Assert.That(session.Domain.FindEntity("Customer")).IsNotNull();
    }

    [Test]
    public async Task Apply_Batch_IsAtomic() {
        var store = new DomainSessionStore();
        var (_, session) = store.Create("Test");

        // First: add the entity
        session.Apply(new AddEntityTypeIntent("Order"));
        // Then: add stages (entity now committed; stages with parents must be in separate applies)
        session.Apply(new AddStageToEntityIntent("Order", "Pending"));
        var result = session.Apply(new AddStageToEntityIntent("Order", "Shipped", "Pending"));

        await Assert.That(result.Succeeded).IsTrue();
        var order = session.Domain.RequireEntity("Order");
        await Assert.That(order.Stages.Count).IsEqualTo(2);
        var shipped = order.RequireStage("Shipped");
        await Assert.That(shipped.Parent?.Name).IsEqualTo("Pending");
    }

    [Test]
    public async Task Apply_RollsBack_OnAnalysisError() {
        var store = new DomainSessionStore();
        var (_, session) = store.Create("Test");

        // AddActionToEntityIntent with a non-existent entity should fail analysis
        var revisionBefore = session.Revision;
        var result = session.Apply(new AddActionToEntityIntent("NonExistentEntity", "DoSomething"));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(session.Revision).IsEqualTo(revisionBefore);
    }

    // ── DomainSession.ToSnapshot ──────────────────────────────────────────────

    [Test]
    public async Task ToSnapshot_ReflectsCurrentDomainState() {
        var store = new DomainSessionStore();
        var (_, session) = store.Create("Shop");
        session.Apply(new AddEntityTypeIntent("Product"));
        session.Apply(new AddPropertyToEntityIntent("Product", "Name", "Text"));

        var snapshot = session.ToSnapshot();

        await Assert.That(snapshot.DomainName).IsEqualTo("Shop");
        var productSnapshot = snapshot.Entities.FirstOrDefault(e => e.Name == "Product");
        await Assert.That(productSnapshot).IsNotNull();
        await Assert.That(productSnapshot!.Properties.Any(p => p.Name == "Name")).IsTrue();
    }

    // ── DomainSession.RenderAsText ────────────────────────────────────────────

    [Test]
    public async Task RenderAsText_ContainsDomainName() {
        var store = new DomainSessionStore();
        var (_, session) = store.Create("My Shop");

        var text = session.RenderAsText();

        await Assert.That(text).Contains("My Shop");
    }

    [Test]
    public async Task RenderEntityAsText_ContainsEntityName() {
        var store = new DomainSessionStore();
        var (_, session) = store.Create("Test");
        session.Apply(new AddEntityTypeIntent("Widget"));

        var text = session.RenderEntityAsText("Widget");

        await Assert.That(text).Contains("Widget");
    }

    // ── DomainDispatcher ──────────────────────────────────────────────────────

    [Test]
    public async Task Dispatcher_AddEntity_UsesSession() {
        var store = new DomainSessionStore();
        var (_, session) = store.Create("Test");
        var dispatcher = new DomainDispatcher(session);

        var result = dispatcher.AddEntity("Invoice");

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(session.Domain.FindEntity("Invoice")).IsNotNull();
    }

    [Test]
    public async Task Dispatcher_AddRelationship_Succeeds() {
        var store = new DomainSessionStore();
        var (_, session) = store.Create("Test");
        var dispatcher = new DomainDispatcher(session);

        dispatcher.AddEntity("Order");
        dispatcher.AddEntity("Item");
        var result = dispatcher.AddRelationship("OrderItems", "Order", "Item", RelationshipCardinality.OneToMany, true);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(session.Domain.FindRelationship("OrderItems")).IsNotNull();
    }

    // ── High-level intent extensions ──────────────────────────────────────────

    [Test]
    public async Task ExtensionMethods_ChainCorrectly() {
        var store = new DomainSessionStore();
        var (_, session) = store.Create("Blog");

        session.AddEntity("Post");
        session.AddProperty("Post", "Title", "Text");
        session.AddStage("Post", "Draft");
        session.AddStage("Post", "Published", "Draft");
        session.AddAction("Post", "Publish");
        session.AddActionToStage("Post", "Draft", "Publish");

        var post = session.Domain.RequireEntity("Post");
        await Assert.That(post.Properties.Count).IsEqualTo(1);
        await Assert.That(post.Stages.Count).IsEqualTo(2);
        await Assert.That(post.Actions.Count).IsEqualTo(1);
        await Assert.That(post.RequireStage("Draft").Actions.Count).IsEqualTo(1);
    }

    // ── TryGetRevisionSnapshot ────────────────────────────────────────────────

    [Test]
    public async Task TryGetRevisionSnapshot_ReturnsSnapshotAtRevision() {
        var store = new DomainSessionStore();
        var (_, session) = store.Create("Test");
        var r0 = session.Revision;

        session.Apply(new AddEntityTypeIntent("Foo"));
        var r1 = session.Revision;

        var foundR0 = session.TryGetRevisionSnapshot(r0, out var snapR0);
        var foundR1 = session.TryGetRevisionSnapshot(r1, out var snapR1);

        await Assert.That(foundR0).IsTrue();
        await Assert.That(foundR1).IsTrue();
        await Assert.That(snapR0).IsNotNull();
        await Assert.That(snapR1).IsNotNull();
    }

    // ── DomainSessionBuilder (internal) ──────────────────────────────────────

    [Test]
    public async Task InternalBuilder_BuildsSessionWithEntitiesAndProperties() {
        var session = new DomainSessionBuilder()
            .WithName("Builder Test")
            .WithEntity("Customer")
            .WithProperty("Customer", "Email", "Text")
            .Build();

        await Assert.That(session.Domain.Name).IsEqualTo("Builder Test");
        var customer = session.Domain.FindEntity("Customer");
        await Assert.That(customer).IsNotNull();
        await Assert.That(customer!.FindProperty("Email")).IsNotNull();
    }

    [Test]
    public async Task InternalBuilder_WithoutBuiltIns_HasNoPrimitives() {
        var session = new DomainSessionBuilder()
            .WithoutBuiltIns()
            .Build();

        await Assert.That(session.Domain.GetAvailablePrimitives().Any()).IsFalse();
    }

    // ── ECommerceDemo ─────────────────────────────────────────────────────────

    [Test]
    public async Task ECommerceDemo_Builds_WithExpectedEntities() {
        var session = ECommerceDemo.Build();
        var domain = session.Domain;

        await Assert.That(domain.FindEntity("Customer")).IsNotNull();
        await Assert.That(domain.FindEntity("Order")).IsNotNull();
        await Assert.That(domain.FindEntity("Product")).IsNotNull();
        await Assert.That(domain.FindEntity("Payment")).IsNotNull();
        await Assert.That(domain.FindEntity("Shipment")).IsNotNull();
        await Assert.That(domain.FindEntity("Review")).IsNotNull();
    }

    [Test]
    public async Task ECommerceDemo_Order_HasExpectedStages() {
        var session = ECommerceDemo.Build();
        var order = session.Domain.RequireEntity("Order");

        var stageNames = order.Stages.Select(static s => s.Name).ToArray();
        await Assert.That(stageNames).Contains("Cart");
        await Assert.That(stageNames).Contains("Pending");
        await Assert.That(stageNames).Contains("Paid");
        await Assert.That(stageNames).Contains("Cancelled");
    }

    [Test]
    public async Task ECommerceDemo_Product_HasExpectedActions() {
        var session = ECommerceDemo.Build();
        var product = session.Domain.RequireEntity("Product");

        var actionNames = product.Actions.Select(static a => a.Name).ToArray();
        await Assert.That(actionNames).Contains("AddProduct");
        await Assert.That(actionNames).Contains("ActivateProduct");
        await Assert.That(actionNames).Contains("UpdateStock");
    }

    [Test]
    public async Task ECommerceDemo_RenderAsText_ContainsEntityNames() {
        var session = ECommerceDemo.Build();
        var text = session.RenderAsText();

        await Assert.That(text).Contains("Order");
        await Assert.That(text).Contains("Customer");
        await Assert.That(text).Contains("Product");
    }

    [Test]
    public async Task ECommerceDemo_Snapshot_HasRelationships() {
        var session = ECommerceDemo.Build();
        var snapshot = session.ToSnapshot();

        await Assert.That(snapshot.Relationships.Any(r => r.Name == "CustomerOrders")).IsTrue();
        await Assert.That(snapshot.Relationships.Any(r => r.Name == "OrderItems")).IsTrue();
    }
}
