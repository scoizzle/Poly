using Poly.DomainModeling;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Queries;
using Poly.Mcp.Sessions;
using Poly.Mcp.Tools;

namespace Poly.Tests.Mcp;

/// <summary>
/// Smoke tests for the V3 MCP tool layer.
/// Tests exercise the curated tool surface end-to-end: session creation,
/// query, and evolution operations — all via the same public API that agents use.
/// </summary>
public class McpSmokeTests {
    [Test]
    public async Task CreateSession_ReturnsSessionIdAndBuiltins() {
        var response = SessionTool.CreateDomainSession("Orders");

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

        var response = QueryTool.GetDomainOverview(sessionId);
        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Data).IsNotNull();
        await Assert.That(response.Data).IsTypeOf<DomainOverviewData>();

        var data = (DomainOverviewData)response.Data!;
        await Assert.That(data.EntityCount).IsEqualTo(0);
        await Assert.That(data.EntityNames).IsEmpty();
        await Assert.That(data.PrimitiveCount).IsGreaterThanOrEqualTo(9);
    }

    [Test]
    public async Task GetDomainAnalysis_ReportsNoErrors_ForValidDomain() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        // Evolve a domain with valid structure
        EvolveTool.AddEntity(sessionId, "Order");
        EvolveTool.AddProperty(sessionId, "Order", "Status", "Text");

        var response = QueryTool.GetDomainAnalysis(sessionId);
        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Data).IsTypeOf<AnalysisData>();

        var data = (AnalysisData)response.Data!;
        await Assert.That(data.ErrorCount).IsEqualTo(0);
    }

    [Test]
    public async Task AddEntityTool_CreatesEntity() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var response = EvolveTool.AddEntity(sessionId, "Order");
        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Revision).IsEqualTo(1);

        // Verify through query tool
        var overviewResponse = QueryTool.GetDomainOverview(sessionId);
        var data = (DomainOverviewData)overviewResponse.Data!;
        await Assert.That(data.EntityCount).IsEqualTo(1);
        await Assert.That(data.EntityNames).Contains("Order");
    }

    [Test]
    public async Task AddEntityTool_DuplicateName_RollsBack() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        // First add succeeds
        var r1 = EvolveTool.AddEntity(sessionId, "Order");
        await Assert.That(r1.Success).IsTrue();

        // Second add with same name rolls back
        var r2 = EvolveTool.AddEntity(sessionId, "Order");
        await Assert.That(r2.Success).IsFalse();
        await Assert.That(r2.Diagnostics).IsNotNull();
        await Assert.That(r2.Diagnostics!.Count).IsGreaterThan(0);

        // Revision should NOT have been bumped on failure
        await Assert.That(r2.Revision).IsEqualTo(1);
    }

    [Test]
    public async Task AddPropertyTool_AddsPropertyToEntity() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        EvolveTool.AddEntity(sessionId, "Order");
        var response = EvolveTool.AddProperty(sessionId, "Order", "Status", "Text");

        await Assert.That(response.Success).IsTrue();

        // Verify through entity detail
        var detailResponse = QueryTool.GetEntityDetail(sessionId, "Order");
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
        var r1 = EvolveTool.AddEntity(sessionId, "Order");
        await Assert.That(r1.Success).IsTrue();

        // Add properties
        var r2 = EvolveTool.AddProperty(sessionId, "Order", "Status", "Text");
        await Assert.That(r2.Success).IsTrue();
        await Assert.That(r2.Revision).IsEqualTo(2);

        var r3 = EvolveTool.AddProperty(sessionId, "Order", "Total", "Number");
        await Assert.That(r3.Success).IsTrue();

        // Add stages
        var r4 = EvolveTool.AddStage(sessionId, "Order", "Draft");
        await Assert.That(r4.Success).IsTrue();

        var r5 = EvolveTool.AddStage(sessionId, "Order", "Submitted");
        await Assert.That(r5.Success).IsTrue();

        // Add action
        var r6 = EvolveTool.AddAction(sessionId, "Order", "Submit");
        await Assert.That(r6.Success).IsTrue();
        await Assert.That(r6.Revision).IsEqualTo(6);

        var r7 = EvolveTool.AddActionToStage(sessionId, "Order", "Draft", "Submit");
        await Assert.That(r7.Success).IsTrue();

        // Get entity detail
        var detailResponse = QueryTool.GetEntityDetail(sessionId, "Order");
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

        var response = SessionTool.ListSessions();
        await Assert.That(response.Success).IsTrue();

        // Should include both session IDs in the message or data
        await Assert.That(response.Message).Contains(id1!);
        await Assert.That(response.Message).Contains(id2!);
    }

    [Test]
    public async Task GetEntityDetail_MissingEntity_ReturnsFailureWithAffordances() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var response = QueryTool.GetEntityDetail(sessionId, "NonExistent");
        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("not found");
        await Assert.That(response.Affordances).IsNotNull();
        await Assert.That(response.Affordances!.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task InvalidSession_ReturnsHelpfulAffordances() {
        var response = QueryTool.GetDomainOverview("nonexistent-session");
        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Affordances).IsNotNull();
        await Assert.That(response.Affordances).Contains("create_domain_session");
    }

    [Test]
    public async Task AddPropertyToMissingEntity_ReportsFailure_WithoutBumpingRevision() {
        var (sessionId, state) = McpSessionStore.Create("Test");
        var originalRevision = state.Revision;

        var response = EvolveTool.AddProperty(sessionId, "NonExistent", "Status", "Text");
        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("Evolution rolled back");
        await Assert.That(response.Revision).IsEqualTo(originalRevision); // not bumped
        await Assert.That(response.Affordances).IsNotNull();
    }

    [Test]
    public async Task AddStageToMissingEntity_ReportsFailure() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        // Add entity, then try adding a stage to a different entity that doesn't exist
        EvolveTool.AddEntity(sessionId, "Order");
        var response = EvolveTool.AddStage(sessionId, "NonExistent", "Draft");

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("Evolution rolled back");
    }

    [Test]
    public async Task AddActionToMissingEntity_ReportsFailure() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        EvolveTool.AddEntity(sessionId, "Order");
        var response = EvolveTool.AddAction(sessionId, "NonExistent", "Submit");

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("Evolution rolled back");
    }

    [Test]
    public async Task GetPolicyExpression_FindsPolicyOnEntity() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        // Build a domain with a policy
        EvolveTool.AddEntity(sessionId, "Person");
        EvolveTool.AddProperty(sessionId, "Person", "Age", "Number");
        EvolveTool.AddEntity(sessionId, "Person"); // no-op duplicate handled

        // Add policy via direct evolve
        var session = McpSessionStore.TryGet(sessionId, out var state);
        var evolve = new DomainEvolution(state.Domain);
        var result = evolve.Evolve()
            .AddPolicyToEntity("Person", "Adult",
                DomainExpression.GreaterThanOrEqual(
                    DomainExpression.Property("Age"),
                    DomainExpression.Literal(18)))
            .Apply();
        if (result.Succeeded)
            McpSessionStore.Update(sessionId, result.Root, result.Analysis);

        var response = PolicyTool.GetPolicyExpression(sessionId, "Person", "Adult");
        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Data).IsNotNull();
    }

    [Test]
    public async Task GetPolicyExpression_MissingPolicy_ReturnsNotFound() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        EvolveTool.AddEntity(sessionId, "Person");

        var response = PolicyTool.GetPolicyExpression(sessionId, "Person", "NonExistent");
        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("not found");
    }

    [Test]
    public async Task GetPolicyExpression_MissingEntity_ReturnsNotFound() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var response = PolicyTool.GetPolicyExpression(sessionId, "NonExistent", "Any");
        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("not found");
    }

    // ── add_policy / evaluate_policy MCP tools (Slice 3) ────────────

    [Test]
    public async Task AddPolicy_SimplePropertyComparison_Succeeds() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        EvolveTool.AddEntity(sessionId, "Person");
        EvolveTool.AddProperty(sessionId, "Person", "Age", "Number");

        var response = PolicyTool.AddPolicy(sessionId, "Person", "Adult",
            property: "Age", op: ">=", value: 18);

        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Message).Contains("Adult");
        await Assert.That(response.Affordances).Contains("evaluate_policy");

        // Verify via get_policy_expression
        var expr = PolicyTool.GetPolicyExpression(sessionId, "Person", "Adult");
        await Assert.That(expr.Success).IsTrue();
    }

    [Test]
    public async Task AddPolicy_ToMissingEntity_Fails() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var response = PolicyTool.AddPolicy(sessionId, "NonExistent", "Any",
            property: "Age", op: ">=", value: 18);

        await Assert.That(response.Success).IsFalse();
    }

    [Test]
    public async Task AddPolicy_InvalidExpression_Fails() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        EvolveTool.AddEntity(sessionId, "Person");

        var response = PolicyTool.AddPolicy(sessionId, "Person", "Bad",
            property: "", op: ">=", value: 18);

        await Assert.That(response.Success).IsFalse();
    }

    [Test]
    public async Task EvaluatePolicy_AgeGuard_ReturnsTrueForAdult() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        EvolveTool.AddEntity(sessionId, "Person");
        EvolveTool.AddProperty(sessionId, "Person", "Age", "Number");

        PolicyTool.AddPolicy(sessionId, "Person", "Adult",
            property: "Age", op: ">=", value: 18);

        var adult = PolicyTool.EvaluatePolicy(sessionId, "Person", "Adult",
            properties: "{\"Age\":25}");
        await Assert.That(adult.Success).IsTrue();
        await Assert.That(adult.Message).Contains("true");

        var minor = PolicyTool.EvaluatePolicy(sessionId, "Person", "Adult",
            properties: "{\"Age\":15}");
        await Assert.That(minor.Success).IsTrue();
        await Assert.That(minor.Message).Contains("false");
    }

    [Test]
    public async Task EvaluatePolicy_MissingPolicy_ReturnsNotFound() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        EvolveTool.AddEntity(sessionId, "Person");

        var response = PolicyTool.EvaluatePolicy(sessionId, "Person", "NonExistent", age: 25);
        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("not found");
    }

    [Test]
    public async Task EvaluatePolicy_MultiProperty_OrderTotalStatus_EvaluatesCorrectly() {
        // Proves evaluate_policy works with non-Age properties via JSON properties arg
        var (sessionId, _) = McpSessionStore.Create("Test");
        EvolveTool.AddEntity(sessionId, "Order");
        EvolveTool.AddProperty(sessionId, "Order", "Total", "Number");
        EvolveTool.AddProperty(sessionId, "Order", "Status", "Text");

        PolicyTool.AddPolicy(sessionId, "Order", "LargeActive",
            and: "[{\"property\":\"Total\",\"op\":\">\",\"value\":100},{\"property\":\"Status\",\"op\":\"==\",\"value\":\"Active\"}]");

        // Pass with Total > 100 and Status == "Active"
        var pass = PolicyTool.EvaluatePolicy(sessionId, "Order", "LargeActive",
            properties: "{\"Total\":200,\"Status\":\"Active\"}");
        await Assert.That(pass.Success).IsTrue();
        await Assert.That(pass.Message).Contains("true");

        // Fail with Total <= 100
        var failTotal = PolicyTool.EvaluatePolicy(sessionId, "Order", "LargeActive",
            properties: "{\"Total\":50,\"Status\":\"Active\"}");
        await Assert.That(failTotal.Success).IsTrue();
        await Assert.That(failTotal.Message).Contains("false");

        // Fail with wrong Status
        var failStatus = PolicyTool.EvaluatePolicy(sessionId, "Order", "LargeActive",
            properties: "{\"Total\":200,\"Status\":\"Cancelled\"}");
        await Assert.That(failStatus.Success).IsTrue();
        await Assert.That(failStatus.Message).Contains("false");
    }

    [Test]
    public async Task EvaluatePolicy_MultiProperty_ProductStock_EvaluatesCorrectly() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        EvolveTool.AddEntity(sessionId, "Product");
        EvolveTool.AddProperty(sessionId, "Product", "Stock", "Number");

        PolicyTool.AddPolicy(sessionId, "Product", "PositiveStock",
            property: "Stock", op: ">=", value: 0);

        var pass = PolicyTool.EvaluatePolicy(sessionId, "Product", "PositiveStock",
            properties: "{\"Stock\":10}");
        await Assert.That(pass.Success).IsTrue();
        await Assert.That(pass.Message).Contains("true");

        var fail = PolicyTool.EvaluatePolicy(sessionId, "Product", "PositiveStock",
            properties: "{\"Stock\":-1}");
        await Assert.That(fail.Success).IsTrue();
        await Assert.That(fail.Message).Contains("false");
    }

    [Test]
    public async Task EvaluatePolicy_InvalidProperty_ReturnsClearError() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        EvolveTool.AddEntity(sessionId, "Person");
        EvolveTool.AddProperty(sessionId, "Person", "Age", "Number");

        PolicyTool.AddPolicy(sessionId, "Person", "Adult",
            property: "Age", op: ">=", value: 18);

        // Providing a property that doesn't exist on the entity
        var response = PolicyTool.EvaluatePolicy(sessionId, "Person", "Adult",
            properties: "{\"NonExistent\":42}");
        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("does not exist on entity");
    }

    // ── ws8-6e: Bool ABI adult assert ────────────────────────────

    [Test]
    public async Task EvaluatePolicy_BooleanGuard_EqualsTrue_ReturnsTrue() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        EvolveTool.AddEntity(sessionId, "Flag");
        EvolveTool.AddProperty(sessionId, "Flag", "Enabled", "Boolean");

        PolicyTool.AddPolicy(sessionId, "Flag", "IsEnabled",
            property: "Enabled", op: "==", value: true);

        var pass = PolicyTool.EvaluatePolicy(sessionId, "Flag", "IsEnabled",
            properties: "{\"Enabled\":true}");
        await Assert.That(pass.Success).IsTrue();
        await Assert.That(pass.Message).Contains("true");

        var fail = PolicyTool.EvaluatePolicy(sessionId, "Flag", "IsEnabled",
            properties: "{\"Enabled\":false}");
        await Assert.That(fail.Success).IsTrue();
        await Assert.That(fail.Message).Contains("false");
    }

    // ── ws8-6f: MatchNumeric positive control ────────────────────

    [Test]
    public async Task EvaluatePolicy_GreaterThanOrEqual_MatchNumeric_ReturnsTrue() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        EvolveTool.AddEntity(sessionId, "Item");
        EvolveTool.AddProperty(sessionId, "Item", "Score", "Number");

        PolicyTool.AddPolicy(sessionId, "Item", "HighScore",
            property: "Score", op: ">=", value: 100);

        var pass = PolicyTool.EvaluatePolicy(sessionId, "Item", "HighScore",
            properties: "{\"Score\":100}");
        await Assert.That(pass.Success).IsTrue();
        await Assert.That(pass.Message).Contains("true");

        var fail = PolicyTool.EvaluatePolicy(sessionId, "Item", "HighScore",
            properties: "{\"Score\":99}");
        await Assert.That(fail.Success).IsTrue();
        await Assert.That(fail.Message).Contains("false");
    }
}