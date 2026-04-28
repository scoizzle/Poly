using Poly.Mcp;

namespace Poly.Tests.Mcp;

public class DomainMcpToolsTests {
    [Test]
    public async Task ApplyDomainMutation_CreateDomain_ReturnsSessionAndSnapshot() {
        var sessionId = $"mcp-test-{Guid.NewGuid():N}";

        var response = DomainAuthoringTool.ApplyDomainMutation(new DomainMutationRequest(
            Operation: "create_domain",
            SessionId: sessionId,
            DomainName: "Orders"));

        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.SessionId).IsEqualTo(sessionId);
        await Assert.That(response.Snapshot).IsNotNull();
        await Assert.That(response.Snapshot!.DomainName).IsEqualTo("Orders");
    }

    [Test]
    public async Task ApplyDomainMutation_AddEntityAndProperty_UpdatesCapabilitySnapshot() {
        var sessionId = $"mcp-test-{Guid.NewGuid():N}";

        _ = DomainAuthoringTool.ApplyDomainMutation(new DomainMutationRequest(
            Operation: "create_domain",
            SessionId: sessionId,
            DomainName: "Catalog"));

        _ = DomainAuthoringTool.ApplyDomainMutation(new DomainMutationRequest(
            Operation: "add_primitive",
            SessionId: sessionId,
            PrimitiveName: "NameText",
            PrimitiveCategory: "Text"));

        _ = DomainAuthoringTool.ApplyDomainMutation(new DomainMutationRequest(
            Operation: "add_entity",
            SessionId: sessionId,
            EntityName: "Product"));

        var propertyResponse = DomainAuthoringTool.ApplyDomainMutation(new DomainMutationRequest(
            Operation: "add_property_to_entity",
            SessionId: sessionId,
            EntityName: "Product",
            PropertyName: "Name",
            TypeName: "NameText"));

        await Assert.That(propertyResponse.Success).IsTrue();
        await Assert.That(propertyResponse.Snapshot).IsNotNull();

        var product = propertyResponse.Snapshot!.Entities.Single(entity => entity.Name == "Product");
        await Assert.That(product.Properties.Select(property => property.Name)).Contains("Name");
    }

    [Test]
    public async Task ApplyDomainMutation_AddRelationship_AttachesToSourceEntity() {
        var sessionId = $"mcp-test-{Guid.NewGuid():N}";

        _ = DomainAuthoringTool.ApplyDomainMutation(new DomainMutationRequest(
            Operation: "create_domain",
            SessionId: sessionId,
            DomainName: "Sales"));

        _ = DomainAuthoringTool.ApplyDomainMutation(new DomainMutationRequest(
            Operation: "add_entity",
            SessionId: sessionId,
            EntityName: "Customer"));

        _ = DomainAuthoringTool.ApplyDomainMutation(new DomainMutationRequest(
            Operation: "add_entity",
            SessionId: sessionId,
            EntityName: "Order"));

        var relationshipResponse = DomainAuthoringTool.ApplyDomainMutation(new DomainMutationRequest(
            Operation: "add_relationship",
            SessionId: sessionId,
            RelationshipName: "CustomerOrders",
            SourceEntityName: "Customer",
            TargetEntityName: "Order",
            Cardinality: "OneToMany",
            SourceOwnsTarget: true));

        await Assert.That(relationshipResponse.Success).IsTrue();
        await Assert.That(relationshipResponse.Snapshot).IsNotNull();

        var relationship = relationshipResponse.Snapshot!.Relationships.Single(item => item.Name == "CustomerOrders");
        await Assert.That(relationship.Source).IsEqualTo("Customer");
        await Assert.That(relationship.Target).IsEqualTo("Order");
        await Assert.That(relationship.SourceOwnsTarget).IsTrue();

        var customer = relationshipResponse.Snapshot.Entities.Single(entity => entity.Name == "Customer");
        await Assert.That(customer.Relationships).Contains("CustomerOrders");
    }

    [Test]
    public async Task InterrogateDomainCapabilities_WithoutSession_ReturnsSupportedOperations() {
        var response = DomainCapabilityTool.InterrogateDomainCapabilities();

        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.SupportedMutationOperations).Contains("create_domain");
        await Assert.That(response.Snapshot).IsNull();
    }
}