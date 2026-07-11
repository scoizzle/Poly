using Poly.DomainModeling;
using Poly.DomainModeling.Queries;
using Poly.Mcp.Sessions;
using Poly.Mcp.Tools;

namespace Poly.Tests.Mcp;

/// <summary>
/// Smoke tests for the V3 MCP tool layer.
/// Tests exercise the curated tool surface end-to-end: session creation,
/// query, and evolution operations — all via the same public API that agents use.
/// </summary>
public class V3McpSmokeTests {
    [Test]
    public async Task CreateSession_ReturnsSessionIdAndBuiltins() {
        var response = V3SessionTool.CreateDomainSession("Orders");

        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.SessionId).IsNotNull();
        await Assert.That(response.Revision).IsEqualTo(0);
        await Assert.That(response.Message).Contains("Orders");

        // Verify the session actually exists
        var exists = McpSessionStore.TryGet(response.SessionId!, out var state);
        await Assert.That(exists).IsTrue();
        await Assert.That(state.Domain.Name).IsEqualTo("Orders");

        var primitives = state.Domain.Types.OfType<PrimitiveType>().ToList();
        await Assert.That(primitives.Count).IsGreaterThanOrEqualTo(9);
    }

    [Test]
    public async Task GetDomainOverview_AfterCreate_ShowsEmptyDomain() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var response = V3QueryTool.GetDomainOverview(sessionId);
        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Data).IsNotNull();
        await Assert.That(response.Data).IsTypeOf<DomainOverviewData>();

        var data = (DomainOverviewData)response.Data!;
        await Assert.That(data.EntityCount).IsEqualTo(0);
        await Assert.That(data.EntityNames).IsEmpty();
        await Assert.That(data.PrimitiveCount).IsGreaterThanOrEqualTo(9);
    }

    [Test]
    public async Task AddEntityTool_CreatesEntity() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var response = V3EvolveTool.AddEntity(sessionId, "Order");
        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Revision).IsEqualTo(1);

        // Verify through query tool
        var overviewResponse = V3QueryTool.GetDomainOverview(sessionId);
        var data = (DomainOverviewData)overviewResponse.Data!;
        await Assert.That(data.EntityCount).IsEqualTo(1);
        await Assert.That(data.EntityNames).Contains("Order");
    }

    [Test]
    public async Task AddEntityTool_DuplicateName_RollsBack() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        // First add succeeds
        var r1 = V3EvolveTool.AddEntity(sessionId, "Order");
        await Assert.That(r1.Success).IsTrue();

        // Second add with same name rolls back
        var r2 = V3EvolveTool.AddEntity(sessionId, "Order");
        await Assert.That(r2.Success).IsFalse();
        await Assert.That(r2.Diagnostics).IsNotNull();
        await Assert.That(r2.Diagnostics!.Count).IsGreaterThan(0);

        // Revision should NOT have been bumped on failure
        await Assert.That(r2.Revision).IsEqualTo(1);
    }

    [Test]
    public async Task AddPropertyTool_AddsPropertyToEntity() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        V3EvolveTool.AddEntity(sessionId, "Order");
        var response = V3EvolveTool.AddProperty(sessionId, "Order", "Status", "Text");

        await Assert.That(response.Success).IsTrue();

        // Verify through entity detail
        var detailResponse = V3QueryTool.GetEntityDetail(sessionId, "Order");
        await Assert.That(detailResponse.Data).IsTypeOf<EntityDetailData>();
        var detail = (EntityDetailData)detailResponse.Data!;
        await Assert.That(detail.Properties.Count).IsEqualTo(1);
        await Assert.That(detail.Properties[0].Name).IsEqualTo("Status");
        await Assert.That(detail.Properties[0].TypeName).IsEqualTo("Text");
    }

    [Test]
    public async Task FullAgentPath_CreateToEntityDetail() {
        // Simulate an agent workflow: create → add entity → add property → add stage → add action → get detail
        var (sessionId, _) = McpSessionStore.Create("Orders");

        // Add entity
        var r1 = V3EvolveTool.AddEntity(sessionId, "Order");
        await Assert.That(r1.Success).IsTrue();

        // Add properties
        var r2 = V3EvolveTool.AddProperty(sessionId, "Order", "Status", "Text");
        await Assert.That(r2.Success).IsTrue();
        await Assert.That(r2.Revision).IsEqualTo(2);

        var r3 = V3EvolveTool.AddProperty(sessionId, "Order", "Total", "Number");
        await Assert.That(r3.Success).IsTrue();

        // Add stages
        var r4 = V3EvolveTool.AddStage(sessionId, "Order", "Draft");
        await Assert.That(r4.Success).IsTrue();

        var r5 = V3EvolveTool.AddStage(sessionId, "Order", "Submitted");
        await Assert.That(r5.Success).IsTrue();

        // Add action
        var r6 = V3EvolveTool.AddAction(sessionId, "Order", "Submit");
        await Assert.That(r6.Success).IsTrue();
        await Assert.That(r6.Revision).IsEqualTo(6);

        var r7 = V3EvolveTool.AddActionToStage(sessionId, "Order", "Draft", "Submit");
        await Assert.That(r7.Success).IsTrue();

        // Get entity detail
        var detailResponse = V3QueryTool.GetEntityDetail(sessionId, "Order");
        await Assert.That(detailResponse.Data).IsTypeOf<EntityDetailData>();
        var detail = (EntityDetailData)detailResponse.Data!;

        await Assert.That(detail.Properties.Count).IsEqualTo(2);
        await Assert.That(detail.Stages.Count).IsEqualTo(2);
        await Assert.That(detail.Actions.Count).IsEqualTo(1);
        await Assert.That(detail.Actions[0].Name).IsEqualTo("Submit");
        await Assert.That(detail.Policies).IsEmpty();

        // Final revision should be 7
        await Assert.That(detailResponse.Revision).IsEqualTo(7);
    }

    [Test]
    public async Task ListSessions_ReturnsActiveSessions() {
        var (id1, _) = McpSessionStore.Create("A");
        var (id2, _) = McpSessionStore.Create("B");

        var response = V3SessionTool.ListSessions();
        await Assert.That(response.Success).IsTrue();

        // Should include both session IDs in the message or data
        await Assert.That(response.Message).Contains(id1!);
        await Assert.That(response.Message).Contains(id2!);
    }

    [Test]
    public async Task GetEntityDetail_MissingEntity_ReturnsFailureWithAffordances() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var response = V3QueryTool.GetEntityDetail(sessionId, "NonExistent");
        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("not found");
        await Assert.That(response.Affordances).IsNotNull();
        await Assert.That(response.Affordances!.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task InvalidSession_ReturnsHelpfulAffordances() {
        var response = V3QueryTool.GetDomainOverview("nonexistent-session");
        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Affordances).IsNotNull();
        await Assert.That(response.Affordances).Contains("create_domain_session");
    }
}