using Poly.Mcp;

namespace Poly.Tests.Mcp;

public class DomainMcpToolsTests {
    [Test]
    public async Task CreateDomain_ReturnsSessionAndAffordances() {
        var sessionId = $"mcp-test-{Guid.NewGuid():N}";

        var response = DomainAuthoringTool.CreateDomain("Orders", sessionId);

        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.SessionId).IsEqualTo(sessionId);
        await Assert.That(response.DomainName).IsEqualTo("Orders");
        await Assert.That(response.Affordances.Select(item => item.Tool)).Contains(nameof(DomainQueryTool.GetDomainOverview));
    }

    [Test]
    public async Task AddEntityAndProperty_IterativeQueryReturnsEntityDetails() {
        var sessionId = $"mcp-test-{Guid.NewGuid():N}";

        _ = DomainAuthoringTool.CreateDomain("Catalog", sessionId);
        _ = DomainAuthoringTool.AddPrimitive(sessionId, "NameText", "Text");
        _ = DomainAuthoringTool.AddEntity(sessionId, "Product");
        var propertyResponse = DomainAuthoringTool.AddPropertyToEntity(sessionId, "Product", "Name", "NameText");
        var entityQuery = DomainQueryTool.GetEntity(sessionId, "Product");

        await Assert.That(propertyResponse.Success).IsTrue();
        await Assert.That(entityQuery.Success).IsTrue();
        await Assert.That(entityQuery.Data).IsNotNull();

        var product = entityQuery.Data!;
        await Assert.That(product.Name).IsEqualTo("Product");
        await Assert.That(product.Properties.Select(property => property.Name)).Contains("Name");
    }

    [Test]
    public async Task AddRelationship_IterativeQueriesExposeRelationshipAndEntityReference() {
        var sessionId = $"mcp-test-{Guid.NewGuid():N}";

        _ = DomainAuthoringTool.CreateDomain("Sales", sessionId);
        _ = DomainAuthoringTool.AddEntity(sessionId, "Customer");
        _ = DomainAuthoringTool.AddEntity(sessionId, "Order");

        var relationshipResponse = DomainAuthoringTool.AddRelationship(
            sessionId, "CustomerOrders", "Customer", "Order", "OneToMany", sourceOwnsTarget: true);
        var relationshipQuery = DomainQueryTool.GetRelationship(sessionId, "CustomerOrders");
        var customerQuery = DomainQueryTool.GetEntity(sessionId, "Customer", includeActions: false, includeStages: false);

        await Assert.That(relationshipResponse.Success).IsTrue();
        await Assert.That(relationshipQuery.Success).IsTrue();
        await Assert.That(relationshipQuery.Data).IsNotNull();
        await Assert.That(customerQuery.Success).IsTrue();
        await Assert.That(customerQuery.Data).IsNotNull();

        var relationship = relationshipQuery.Data!;
        await Assert.That(relationship.Source).IsEqualTo("Customer");
        await Assert.That(relationship.Target).IsEqualTo("Order");
        await Assert.That(relationship.SourceOwnsTarget).IsTrue();

        var customer = customerQuery.Data!;
        await Assert.That(customer.Relationships).Contains("CustomerOrders");
    }

    [Test]
    public async Task InterrogateDomainCapabilities_WithoutSession_ReturnsSuccess() {
        var response = DomainCapabilityTool.InterrogateDomainCapabilities();

        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Overview).IsNull();
        await Assert.That(response.Affordances.Select(item => item.Tool)).Contains(nameof(DomainAuthoringTool.CreateDomain));
    }
}