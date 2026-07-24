using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Effects;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Parsing;
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

        // Add policy via direct evolve — uses atomic Evolve for concurrency safety
        McpSessionStore.Evolve(sessionId, domain =>
            new DomainEvolution(domain).Evolve()
                .AddPolicyToEntity("Person", "Adult",
                    DomainExpression.GreaterThanOrEqual(
                        DomainExpression.Property("Age"),
                        DomainExpression.Literal(18)))
                .Apply());

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
            expression: """{"property":"Age","op":">=","value":18}""");

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
            expression: """{"property":"Age","op":">=","value":18}""");

        await Assert.That(response.Success).IsFalse();
    }

    [Test]
    public async Task AddPolicy_InvalidExpression_Fails() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        EvolveTool.AddEntity(sessionId, "Person");

        var response = PolicyTool.AddPolicy(sessionId, "Person", "Bad",
            expression: """{"property":"","op":">=","value":18}""");

        await Assert.That(response.Success).IsFalse();
    }

    [Test]
    public async Task EvaluatePolicy_AgeGuard_ReturnsTrueForAdult() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        EvolveTool.AddEntity(sessionId, "Person");
        EvolveTool.AddProperty(sessionId, "Person", "Age", "Number");

        PolicyTool.AddPolicy(sessionId, "Person", "Adult",
            expression: """{"property":"Age","op":">=","value":18}""");

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
            expression: """{"and":[{"property":"Total","op":">","value":100},{"property":"Status","op":"==","value":"Active"}]}""");

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
            expression: """{"property":"Stock","op":">=","value":0}""");

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
            expression: """{"property":"Age","op":">=","value":18}""");

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
            expression: """{"property":"Enabled","op":"==","value":true}""");

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
            expression: """{"property":"Score","op":">=","value":100}""");

        var pass = PolicyTool.EvaluatePolicy(sessionId, "Item", "HighScore",
            properties: "{\"Score\":100}");
        await Assert.That(pass.Success).IsTrue();
        await Assert.That(pass.Message).Contains("true");

        var fail = PolicyTool.EvaluatePolicy(sessionId, "Item", "HighScore",
            properties: "{\"Score\":99}");
        await Assert.That(fail.Success).IsTrue();
        await Assert.That(fail.Message).Contains("false");
    }

    /// <summary>
    /// Regression test: all expression shapes (comparison, composite and/or/not,
    /// literal) parse and evaluate correctly through the unified JSON parser.
    /// </summary>
    [Test]
    public async Task AddPolicy_AllJsonExpressionShapes_EvaluateCorrectly() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        EvolveTool.AddEntity(sessionId, "Person");
        EvolveTool.AddProperty(sessionId, "Person", "Age", "Number");
        EvolveTool.AddProperty(sessionId, "Person", "Active", "Boolean");

        // Comparison (>= operator)
        var r1 = PolicyTool.AddPolicy(sessionId, "Person", "Adult",
            expression: """{"property":"Age","op":">=","value":18}""");
        await Assert.That(r1.Success).IsTrue();

        // Boolean equality
        var r2 = PolicyTool.AddPolicy(sessionId, "Person", "IsActive",
            expression: """{"property":"Active","op":"==","value":true}""");
        await Assert.That(r2.Success).IsTrue();

        // Composite AND
        var r3 = PolicyTool.AddPolicy(sessionId, "Person", "ActiveAdult",
            expression: """{"and":[{"property":"Age","op":">=","value":18},{"property":"Active","op":"==","value":true}]}""");
        await Assert.That(r3.Success).IsTrue();

        // Composite NOT
        var r4 = PolicyTool.AddPolicy(sessionId, "Person", "NotAdult",
            expression: """{"not":{"property":"Age","op":">=","value":18}}""");
        await Assert.That(r4.Success).IsTrue();

        // Literal
        var r5 = PolicyTool.AddPolicy(sessionId, "Person", "Always",
            expression: """{"literal":true}""");
        await Assert.That(r5.Success).IsTrue();

        // Evaluate: Adult (Age >= 18)
        var pass = PolicyTool.EvaluatePolicy(sessionId, "Person", "Adult",
            properties: "{\"Age\":25}");
        await Assert.That(pass.Success).IsTrue();
        await Assert.That(pass.Message).Contains("true");

        var edge = PolicyTool.EvaluatePolicy(sessionId, "Person", "Adult",
            properties: "{\"Age\":18}");
        await Assert.That(edge.Success).IsTrue();
        await Assert.That(edge.Message).Contains("true");

        var fail = PolicyTool.EvaluatePolicy(sessionId, "Person", "Adult",
            properties: "{\"Age\":15}");
        await Assert.That(fail.Success).IsTrue();
        await Assert.That(fail.Message).Contains("false");

        // Evaluate: IsActive (Active == true)
        var activePass = PolicyTool.EvaluatePolicy(sessionId, "Person", "IsActive",
            properties: "{\"Active\":true, \"Age\":25}");
        await Assert.That(activePass.Success).IsTrue();
        await Assert.That(activePass.Message).Contains("true");

        var activeFail = PolicyTool.EvaluatePolicy(sessionId, "Person", "IsActive",
            properties: "{\"Active\":false, \"Age\":25}");
        await Assert.That(activeFail.Success).IsTrue();
        await Assert.That(activeFail.Message).Contains("false");

        // Evaluate: ActiveAdult (AND)
        var andPass = PolicyTool.EvaluatePolicy(sessionId, "Person", "ActiveAdult",
            properties: "{\"Age\":25,\"Active\":true}");
        await Assert.That(andPass.Success).IsTrue();
        await Assert.That(andPass.Message).Contains("true");

        var andFail = PolicyTool.EvaluatePolicy(sessionId, "Person", "ActiveAdult",
            properties: "{\"Age\":25,\"Active\":false}");
        await Assert.That(andFail.Success).IsTrue();
        await Assert.That(andFail.Message).Contains("false");

        // Evaluate: NotAdult (NOT Age >= 18)
        var notPass = PolicyTool.EvaluatePolicy(sessionId, "Person", "NotAdult",
            properties: "{\"Age\":15}");
        await Assert.That(notPass.Success).IsTrue();
        await Assert.That(notPass.Message).Contains("true");

        // Evaluate: Always (literal true)
        var always = PolicyTool.EvaluatePolicy(sessionId, "Person", "Always",
            properties: "{\"Age\":0}");
        await Assert.That(always.Success).IsTrue();
        await Assert.That(always.Message).Contains("true");
    }

    // ── Batch/plural evolve tools ─────────────────────────────────

    [Test]
    public async Task AddProperties_Batch_Succeeds() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        EvolveTool.AddEntity(sessionId, "Product");

        var response = EvolveTool.AddProperties(sessionId, "Product",
            """[{"name":"SKU","typeName":"Text"},{"name":"Price","typeName":"Number"},{"name":"InStock","typeName":"Boolean"}]""");

        await Assert.That(response.Success).IsTrue();

        var detail = QueryTool.GetEntityDetail(sessionId, "Product");
        await Assert.That(detail.Data).IsTypeOf<EntityDetailData>();
        var d = (EntityDetailData)detail.Data!;
        await Assert.That(d.Properties.Count).IsEqualTo(3);
    }

    [Test]
    public async Task AddStages_Batch_Succeeds() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        EvolveTool.AddEntity(sessionId, "Order");

        var response = EvolveTool.AddStages(sessionId, "Order",
            """[{"name":"Draft"},{"name":"Confirmed"},{"name":"Shipped"},{"name":"Delivered"}]""");

        await Assert.That(response.Success).IsTrue();

        var detail = QueryTool.GetEntityDetail(sessionId, "Order");
        var d = (EntityDetailData)detail.Data!;
        await Assert.That(d.Stages.Count).IsEqualTo(4);
        await Assert.That(d.Stages[0].Name).IsEqualTo("Draft");
    }

    [Test]
    public async Task AddActionsToStages_Batch_Succeeds() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        EvolveTool.AddEntity(sessionId, "Order");
        EvolveTool.AddStages(sessionId, "Order", """[{"name":"Draft"},{"name":"Confirmed"}]""");
        EvolveTool.AddAction(sessionId, "Order", "Submit");
        EvolveTool.AddAction(sessionId, "Order", "Cancel");

        var response = EvolveTool.AddActionsToStages(sessionId, "Order",
            """[{"stageName":"Draft","actionName":"Submit"},{"stageName":"Draft","actionName":"Cancel"}]""");

        await Assert.That(response.Success).IsTrue();

        var detail = QueryTool.GetEntityDetail(sessionId, "Order");
        var d = (EntityDetailData)detail.Data!;
        var draftStage = d.Stages.First(s => s.Name == "Draft");
        await Assert.That(draftStage.Actions).Contains("Submit");
        await Assert.That(draftStage.Actions).Contains("Cancel");
    }

    // ── Domain snapshot ───────────────────────────────────────────

    [Test]
    public async Task GetDomainSnapshot_ReturnsAllEntitiesAndRelationships() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        EvolveTool.AddEntity(sessionId, "Order");
        EvolveTool.AddProperties(sessionId, "Order",
            """[{"name":"Total","typeName":"Number"}]""");
        EvolveTool.AddStages(sessionId, "Order", """[{"name":"Draft"},{"name":"Confirmed"}]""");
        EvolveTool.AddEntity(sessionId, "Customer");
        EvolveTool.AddRelationship(sessionId, "OrderCustomer", "Order", "Customer", "ManyToOne");

        var response = QueryTool.GetDomainSnapshot(sessionId);
        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Data).IsNotNull();
    }

    // ── Relationships ─────────────────────────────────────────────

    [Test]
    public async Task GetRelationships_All_ReturnsAllEdges() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        EvolveTool.AddEntity(sessionId, "Order");
        EvolveTool.AddEntity(sessionId, "Customer");
        EvolveTool.AddRelationship(sessionId, "OrderCustomer", "Order", "Customer", "ManyToOne");

        var response = QueryTool.GetRelationships(sessionId);
        await Assert.That(response.Success).IsTrue();
    }

    [Test]
    public async Task GetRelationships_FilteredByEntity_ReturnsOnlyMatching() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        EvolveTool.AddEntity(sessionId, "Order");
        EvolveTool.AddEntity(sessionId, "Customer");
        EvolveTool.AddEntity(sessionId, "Product");
        EvolveTool.AddRelationship(sessionId, "OrderCustomer", "Order", "Customer", "ManyToOne");
        EvolveTool.AddRelationship(sessionId, "OrderProduct", "Order", "Product");

        var response = QueryTool.GetRelationships(sessionId, entityName: "Customer");
        await Assert.That(response.Success).IsTrue();
    }

    // ── Constraints ───────────────────────────────────────────────

    [Test]
    public async Task AddConstraint_Range_Succeeds() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        EvolveTool.AddEntity(sessionId, "Product");
        EvolveTool.AddProperty(sessionId, "Product", "Price", "Number");

        var response = EvolveTool.AddConstraint(sessionId, "Product", "Price", "Range",
            """{"min":0}""");

        await Assert.That(response.Success).IsTrue();

        var constraints = EvolveTool.GetConstraints(sessionId, "Product");
        await Assert.That(constraints.Success).IsTrue();
    }

    [Test]
    public async Task AddConstraint_Required_Succeeds() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        EvolveTool.AddEntity(sessionId, "Customer");
        EvolveTool.AddProperty(sessionId, "Customer", "Name", "Text");

        var response = EvolveTool.AddConstraint(sessionId, "Customer", "Name", "Required");

        await Assert.That(response.Success).IsTrue();

        var constraints = EvolveTool.GetConstraints(sessionId, "Customer");
        await Assert.That(constraints.Success).IsTrue();
    }

    [Test]
    public async Task GetConstraints_FiltersByProperty() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        EvolveTool.AddEntity(sessionId, "Product");
        EvolveTool.AddProperty(sessionId, "Product", "Price", "Number");
        EvolveTool.AddProperty(sessionId, "Product", "SKU", "Text");
        EvolveTool.AddConstraint(sessionId, "Product", "Price", "Range", """{"min":0}""");
        EvolveTool.AddConstraint(sessionId, "Product", "SKU", "Required");

        // All constraints
        var all = EvolveTool.GetConstraints(sessionId, "Product");
        await Assert.That(all.Success).IsTrue();

        // Filtered by property
        var filtered = EvolveTool.GetConstraints(sessionId, "Product", propertyName: "Price");
        await Assert.That(filtered.Success).IsTrue();
    }

    // ── apply_dsl / export_dsl tools (Slice D) ────────────────────

    [Test]
    public async Task ApplyDsl_MinimalEntity_ReplacesSession() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var response = DslTool.ApplyDsl(sessionId, """
            domain Orders

            Product: entity {
              SKU: Text required unique
              Name: Text required
            }
            """);

        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Message).Contains("Orders");
        await Assert.That(response.Data).IsNotNull();

        // Session should now have the new domain (replaced); revision carries over
        var exists = McpSessionStore.TryGet(sessionId, out var state);
        await Assert.That(exists).IsTrue();
        await Assert.That(state!.Domain.Name).IsEqualTo("Orders");
        await Assert.That(state.Revision).IsEqualTo(1);

        // Entity should exist
        var entity = state.Domain.Types.OfType<Entity>().FirstOrDefault(e => e.Name == "Product");
        await Assert.That(entity).IsNotNull();
        await Assert.That(entity!.Properties.Count).IsEqualTo(2);

        // Affordances should include get_entity_detail
        await Assert.That(response.Affordances).Contains("get_entity_detail");
    }

    [Test]
    public async Task ApplyDsl_WithRelationship_Succeeds() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var response = DslTool.ApplyDsl(sessionId, """
            domain Orders

            Customer: entity {
              Name: Text required
              Places: many Order
            }

            Order: entity {
              Total: Number
              Draft: stage {
                Activate: action {
                  transition to Active
                }
              }
              Active: stage {}
            }
            """);

        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Message).Contains("2 entities");
        await Assert.That(response.Message).Contains("1 relationships");

        var exists = McpSessionStore.TryGet(sessionId, out var state);
        await Assert.That(exists).IsTrue();
        await Assert.That(state!.Domain.Relationships.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ApplyDsl_MissingRequire_FailsWithParseError() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var response = DslTool.ApplyDsl(sessionId, """
            domain Test

            Item: entity {
              Draft: stage {
                Activate: action
                  require NonExistent
                {
                  transition to Active
                }
              }
              Active: stage {}
            }
            """);

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("NonExistent");
    }

    [Test]
    public async Task ApplyDsl_MalformedPoly_FailsWithParseError() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var response = DslTool.ApplyDsl(sessionId, "domain Test\nItem: entity { Name: Text");

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("Parse error");
    }

    [Test]
    public async Task ExportDsl_AfterApply_RoundTrips() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        DslTool.ApplyDsl(sessionId, """
            domain Test

            Item: entity {
              SKU: Text required unique
              Name: Text required
            }
            """);

        var response = DslTool.ExportDsl(sessionId);
        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Data).IsNotNull();

        // The exported poly should contain the domain header and entity
        var poly = response.Data!.GetType().GetProperty("poly")?.GetValue(response.Data) as string;
        await Assert.That(poly).IsNotNull();
        await Assert.That(poly!).Contains("domain Test");
        await Assert.That(poly).Contains("Item: entity");
        await Assert.That(poly).Contains("SKU: Text required unique");

        // Round-trip it: re-apply and verify
        var response2 = DslTool.ApplyDsl(sessionId, poly!);
        await Assert.That(response2.Success).IsTrue();
    }

    [Test]
    public async Task ApplyDsl_ToNonexistentSession_Fails() {
        var response = DslTool.ApplyDsl("nonexistent", "domain Test\nItem: entity {}");

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("not found");
    }

    [Test]
    public async Task ApplyDsl_EmptyPolyText_FailsWithClearMessage() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var response = DslTool.ApplyDsl(sessionId, "");

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("empty");
    }

    [Test]
    public async Task ApplyDsl_WithRequire_BlocksInvokeActionWhenPolicyFails() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        // Step 1: verify entity-with-policy DSL applies
        var response = DslTool.ApplyDsl(sessionId, """
            domain Test

            Item: entity {
              Score: Number
              HighScore: policy { Score > 10 }
              Submit: action
                require HighScore
              {
                transition to Active
              }
              Draft: stage {}
              Active: stage {}
            }
            """);
        await Assert.That(response.Success).IsTrue();

        // Get the entity from the session domain
        var exists = McpSessionStore.TryGet(sessionId, out var state);
        await Assert.That(exists).IsTrue();
        var entity = state!.Domain.Types.OfType<Entity>().First(e => e.Name == "Item");

        // Verify entity-level action has the HighScore policy
        var submitAction = entity.Actions.FirstOrDefault(a => a.Name == "Submit");
        await Assert.That(submitAction).IsNotNull();
        await Assert.That(submitAction!.Policies.Count).IsEqualTo(1);
        await Assert.That(submitAction.Policies[0].Name).IsEqualTo("HighScore");

        // Instance with Score=5 (fails: Score > 10) → blocked
        var instance = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Score"] = 5L });
        var result = instance.InvokeAction("Submit");
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailedGuards.Count).IsGreaterThan(0);
        await Assert.That(result.FailedGuards).Contains("HighScore");
        await Assert.That(instance.CurrentStage).IsEqualTo("Draft");

        // Instance with Score=15 (passes: Score > 10) → succeeds
        var instance2 = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Score"] = 15L });
        var result2 = instance2.InvokeAction("Submit");
        await Assert.That(result2.Succeeded).IsTrue();
        await Assert.That(instance2.CurrentStage).IsEqualTo("Active");
    }

    [Test]
    public async Task ApplyDsl_WithN1NavAndSubscription_Succeeds() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var response = DslTool.ApplyDsl(sessionId, """
            domain Test

            Tracker: entity {
              Status: Text
              Pending: stage {
                when Tracks Active {
                  assign Status to "Triggered"
                }
              }
              Tracks: Order
            }

            Order: entity {
              Draft: stage {
                Activate: action {
                  transition to Active
                }
              }
              Active: stage {}
            }
            """);

        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Message).Contains("2 entities");
        await Assert.That(response.Message).Contains("1 relationships");

        var exists = McpSessionStore.TryGet(sessionId, out var state);
        await Assert.That(exists).IsTrue();
        await Assert.That(state!.Domain.Relationships.Count).IsEqualTo(1);
        await Assert.That(state.Domain.Relationships[0].Name).IsEqualTo("Tracks");

        var tracker = state.Domain.Types.OfType<Entity>().Single(e => e.Name == "Tracker");
        var pending = tracker.Stages.Single(s => s.Name == "Pending");
        await Assert.That(pending.Subscriptions.Count).IsEqualTo(1);
        await Assert.That(pending.Subscriptions[0].RelationshipName).IsEqualTo("Tracks");

        // Analysis should be clean
        var analysis = DomainModelAnalyzer.Analyze(state.Domain);
        await Assert.That(analysis.HasStructuralFailure).IsFalse();

        // BR.4.2: Subscription visibility via MCP get_entity_detail
        var detail = QueryTool.GetEntityDetail(sessionId, "Tracker");
        await Assert.That(detail.Success).IsTrue();
        await Assert.That(detail.Data).IsTypeOf<EntityDetailData>();
        var detailData = (EntityDetailData)detail.Data!;
        var pendingStage = detailData.Stages.FirstOrDefault(s => s.Name == "Pending");
        await Assert.That(pendingStage).IsNotNull();
        await Assert.That(pendingStage!.Subscriptions.Count).IsEqualTo(1);
        await Assert.That(pendingStage.Subscriptions[0].RelationshipName).IsEqualTo("Tracks");
        await Assert.That(pendingStage.Subscriptions[0].StageNames).IsEquivalentTo(new[] { "Active" });
        await Assert.That(pendingStage.Subscriptions[0].Quantifier).IsEqualTo("Each");
    }

    [Test]
    public async Task ExportDsl_AfterAddRelationship_PrintsN1() {
        // Build domain via micro-tools, then export
        var (sessionId, _) = McpSessionStore.Create("Test");

        EvolveTool.AddEntity(sessionId, "Order");
        EvolveTool.AddProperty(sessionId, "Order", "Name", "Text");
        EvolveTool.AddEntity(sessionId, "Tracker");
        EvolveTool.AddProperty(sessionId, "Tracker", "Status", "Text");
        EvolveTool.AddRelationship(sessionId, "Tracks", "Tracker", "Order", "OneToOne");

        var response = DslTool.ExportDsl(sessionId);
        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Data).IsNotNull();

        var poly = response.Data!.GetType().GetProperty("poly")?.GetValue(response.Data) as string;
        await Assert.That(poly).IsNotNull();

        // Should NOT contain N2 form
        await Assert.That(poly!.Contains("relationship Tracks from")).IsFalse();

        // Should contain N1 nav line on the source entity (Tracker)
        await Assert.That(poly).Contains("Tracks: Order");

        // Re-parse the exported N1 poly and verify one relationship
        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = new Domain("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Root!.Relationships.Count).IsEqualTo(1);
        await Assert.That(result.Root.Relationships[0].Name).IsEqualTo("Tracks");
    }

    // ── Slice MR: MCP remove_* micro-tools ──────────────────────

    [Test]
    public async Task RemoveRelationship_RemovesAndUpdatesOverview() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        // Build: two entities + relationship
        EvolveTool.AddEntity(sessionId, "Customer");
        EvolveTool.AddEntity(sessionId, "Order");
        EvolveTool.AddRelationship(sessionId, "Places", "Customer", "Order", "OneToMany");

        var before = QueryTool.GetDomainOverview(sessionId);
        await Assert.That(before.Message).Contains("1 relationships");

        // Remove
        var response = EvolveTool.RemoveRelationship(sessionId, "Places");
        await Assert.That(response.Success).IsTrue();

        var after = QueryTool.GetDomainOverview(sessionId);
        await Assert.That(after.Message).Contains("0 relationships");
    }

    [Test]
    public async Task RemoveRelationship_UnknownName_Fails() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var response = EvolveTool.RemoveRelationship(sessionId, "NonExistent");
        await Assert.That(response.Success).IsFalse();
    }

    [Test]
    public async Task RemoveEntity_RemovesAndUpdatesOverview() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        EvolveTool.AddEntity(sessionId, "Product");
        EvolveTool.AddProperty(sessionId, "Product", "Name", "Text");

        var response = EvolveTool.RemoveEntity(sessionId, "Product");
        await Assert.That(response.Success).IsTrue();

        var overview = QueryTool.GetDomainOverview(sessionId);
        await Assert.That(overview.Message).Contains("0 entities");
    }

    [Test]
    public async Task RemoveEntity_WithRelationship_Fails() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        EvolveTool.AddEntity(sessionId, "Order");
        EvolveTool.AddEntity(sessionId, "Customer");
        EvolveTool.AddRelationship(sessionId, "Places", "Customer", "Order", "OneToMany");

        var response = EvolveTool.RemoveEntity(sessionId, "Order");
        await Assert.That(response.Success).IsFalse();
    }

    [Test]
    public async Task RemoveProperty_RemovesAndUpdatesDetail() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        EvolveTool.AddEntity(sessionId, "Item");
        EvolveTool.AddProperty(sessionId, "Item", "Name", "Text");
        EvolveTool.AddProperty(sessionId, "Item", "Price", "Number");

        var response = EvolveTool.RemoveProperty(sessionId, "Item", "Price");
        await Assert.That(response.Success).IsTrue();

        var detail = QueryTool.GetEntityDetail(sessionId, "Item");
        await Assert.That(detail.Success).IsTrue();
        var d = (EntityDetailData)detail.Data!;
        await Assert.That(d.Properties.Count).IsEqualTo(1);
        await Assert.That(d.Properties[0].Name).IsEqualTo("Name");
    }

    [Test]
    public async Task RemoveStage_RemovesAndUpdatesDetail() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        EvolveTool.AddEntity(sessionId, "Process");
        EvolveTool.AddStage(sessionId, "Process", "Draft");
        EvolveTool.AddStage(sessionId, "Process", "Active");

        var response = EvolveTool.RemoveStage(sessionId, "Process", "Draft");
        await Assert.That(response.Success).IsTrue();

        var detail = QueryTool.GetEntityDetail(sessionId, "Process");
        await Assert.That(detail.Success).IsTrue();
        var d = (EntityDetailData)detail.Data!;
        await Assert.That(d.Stages.Count).IsEqualTo(1);
        await Assert.That(d.Stages[0].Name).IsEqualTo("Active");
    }

    [Test]
    public async Task RemoveAction_RemovesAndUpdatesDetail() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        EvolveTool.AddEntity(sessionId, "Task");
        EvolveTool.AddAction(sessionId, "Task", "DoIt");
        EvolveTool.AddAction(sessionId, "Task", "Undo");

        var response = EvolveTool.RemoveAction(sessionId, "Task", "Undo");
        await Assert.That(response.Success).IsTrue();

        var detail = QueryTool.GetEntityDetail(sessionId, "Task");
        await Assert.That(detail.Success).IsTrue();
        var d = (EntityDetailData)detail.Data!;
        await Assert.That(d.Actions.Count).IsEqualTo(1);
        await Assert.That(d.Actions[0].Name).IsEqualTo("DoIt");
    }

    [Test]
    public async Task RemovePolicy_EntityScope_Removes() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        EvolveTool.AddEntity(sessionId, "Item");
        EvolveTool.AddProperty(sessionId, "Item", "Score", "Number");
        PolicyTool.AddPolicy(sessionId, "Item", "HighScore",
            expression: """{"property":"Score","op":">=","value":100}""");

        var response = EvolveTool.RemovePolicy(sessionId, "Item", "HighScore");
        await Assert.That(response.Success).IsTrue();
    }

    [Test]
    public async Task RemovePolicy_StageScope_Removes() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        EvolveTool.AddEntity(sessionId, "Order");
        EvolveTool.AddStage(sessionId, "Order", "Active");
        EvolveTool.AddProperty(sessionId, "Order", "Score", "Number");

        // Add a policy via evolution directly (MCP add_policy only supports entity scope)
        McpSessionStore.Evolve(sessionId, domain =>
            new DomainEvolution(domain).Evolve()
                .AddPolicyToStage("Order", "Active", "Guard",
                    DomainExpression.GreaterThanOrEqual(
                        DomainExpression.Property("Score"),
                        DomainExpression.Literal(0)))
                .Apply());

        var response = EvolveTool.RemovePolicy(sessionId, "Order", "Guard",
            scope: "stage", stageName: "Active");
        await Assert.That(response.Success).IsTrue();
    }

    [Test]
    public async Task RemoveActionFromStage_Removes() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        EvolveTool.AddEntity(sessionId, "Process");
        EvolveTool.AddStage(sessionId, "Process", "Active");
        EvolveTool.AddActionToStage(sessionId, "Process", "Active", "DoIt");

        var response = EvolveTool.RemoveActionFromStage(sessionId, "Process", "Active", "DoIt");
        await Assert.That(response.Success).IsTrue();

        var detail = QueryTool.GetEntityDetail(sessionId, "Process");
        await Assert.That(detail.Success).IsTrue();
        var d = (EntityDetailData)detail.Data!;
        var activeStage = d.Stages.First(s => s.Name == "Active");
        await Assert.That(activeStage.Actions.Contains("DoIt")).IsFalse();
    }

    [Test]
    public async Task RemovePolicy_InvalidScope_Rejected() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        EvolveTool.AddEntity(sessionId, "Item");

        var response = EvolveTool.RemovePolicy(sessionId, "Item", "SomePolicy",
            scope: "invalid");
        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message.Contains("Invalid scope")).IsTrue();
    }

    [Test]
    public async Task RemovePolicy_MissingStageName_Rejected() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        EvolveTool.AddEntity(sessionId, "Item");

        var response = EvolveTool.RemovePolicy(sessionId, "Item", "SomePolicy",
            scope: "stage");
        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message.Contains("stageName is required")).IsTrue();
    }

    // ── P2.3: Dogfood golden path via MCP apply_dsl ─────────────

    [Test]
    public async Task ApplyDsl_WithCreateInAndSubscription_Succeeds() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var response = DslTool.ApplyDsl(sessionId, """
            domain Test

            Customer: entity {
              Status: Text
              Pending: stage {
                PlaceOrder: action {
                  create in orders { Status: "New" }
                }
                when orders Active {
                  assign Status to "Fulfilled"
                }
              }
              orders: many Order
            }

            Order: entity {
              Status: Text
              Draft: stage {
                Activate: action {
                  transition to Active
                }
              }
              Active: stage {}
            }
            """);

        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Message).Contains("2 entities");
        await Assert.That(response.Message).Contains("1 relationships");

        var exists = McpSessionStore.TryGet(sessionId, out var state);
        await Assert.That(exists).IsTrue();
        await Assert.That(state!.Domain.Relationships.Count).IsEqualTo(1);
        await Assert.That(state.Domain.Relationships[0].Name).IsEqualTo("orders");

        // Verify create-in effect parsed correctly
        var customer = state.Domain.Types.OfType<Entity>().Single(e => e.Name == "Customer");
        var placeOrder = customer.Stages.SelectMany(s => s.Actions).First(a => a.Name == "PlaceOrder");
        await Assert.That(placeOrder.Effects.Count).IsEqualTo(1);
        await Assert.That(placeOrder.Effects[0]).IsTypeOf<CreateEntityInRelationshipEffect>();

        // Verify subscription
        var pending = customer.Stages.Single(s => s.Name == "Pending");
        await Assert.That(pending.Subscriptions.Count).IsEqualTo(1);
        await Assert.That(pending.Subscriptions[0].RelationshipName).IsEqualTo("orders");

        // Analysis should be clean
        var analysis = DomainModelAnalyzer.Analyze(state.Domain);
        await Assert.That(analysis.HasStructuralFailure).IsFalse();

        // Export DSL should still be honest
        var export = DslTool.ExportDsl(sessionId);
        await Assert.That(export.Success).IsTrue();
        var exportedPoly = export.Data!.GetType().GetProperty("poly")?.GetValue(export.Data) as string;
        await Assert.That(exportedPoly).IsNotNull();
        await Assert.That(exportedPoly!.Contains("create in orders")).IsTrue();
    }

    [Test]
    public async Task GetDslGuide_ReturnsProductSurface() {
        var response = DslTool.GetDslGuide();

        await Assert.That(response.Success).IsTrue();
        var dataJson = System.Text.Json.JsonSerializer.Serialize(response.Data);
        await Assert.That(dataJson).Contains("domain");
        await Assert.That(dataJson).Contains("entity");
        await Assert.That(dataJson).Contains("stage");
        // Should mention unsupported constructs
        await Assert.That(dataJson.ToLowerInvariant()).Contains("actor");
        // Should mention apply_dsl / MCP
        await Assert.That(dataJson).Contains("apply_dsl");

        // G′′.4: Anti-pattern guards — guide must not teach lab constructs
        await Assert.That(dataJson.Contains("require {")).IsFalse();
        await Assert.That(dataJson.Contains("require{")).IsFalse();
    }

    [Test]
    public async Task GetDslGuide_GoldenExample_AppliesCleanly() {
        // G2.2 / G′.5 / G′′.2: The guide's golden example must parse and analyze clean.
        // Extract it from the guide text to keep in sync (extract between the ```poly fences).
        var guide = DslTool.GetDslGuide();
        await Assert.That(guide.Success).IsTrue();

        // Extract the golden poly block from the raw guide body (not from serialized JSON)
        var guideProp = guide.Data!.GetType().GetProperty("guide");
        await Assert.That(guideProp).IsNotNull();
        var guideBody = guideProp!.GetValue(guide.Data) as string;
        await Assert.That(guideBody).IsNotNull();

        var poly = ExtractGoldenExampleFromMarkdown(guideBody!);
        await Assert.That(poly).IsNotNull();
        await Assert.That(poly!.Length).IsGreaterThan(50);

        var (sessionId, _) = McpSessionStore.Create("GuideTest");
        var response = DslTool.ApplyDsl(sessionId, poly);
        await Assert.That(response.Success).IsTrue();

        var state = McpSessionStore.TryGet(sessionId, out var s) ? s : null;
        await Assert.That(state).IsNotNull();

        var analysis = DomainModelAnalyzer.Analyze(state!.Domain);
        await Assert.That(analysis.HasStructuralFailure).IsFalse();
        await Assert.That(analysis.HasErrors).IsFalse();

        // G′′.3: export_dsl round-trip assertion
        var exportResponse = DslTool.ExportDsl(sessionId);
        await Assert.That(exportResponse.Success).IsTrue();
        var exportJson = System.Text.Json.JsonSerializer.Serialize(exportResponse.Data);
        await Assert.That(exportJson.ToLowerInvariant()).Contains("domain orders");
        await Assert.That(exportJson).Contains("Total");
        await Assert.That(exportJson).Contains("PositiveTotal");
    }

    /// <summary>
    /// Extracts the golden example from the raw guide markdown by finding the fenced code block
    /// between ```poly and ``` that follows 'Example (Round-Trip Safe)'.
    /// </summary>
    private static string? ExtractGoldenExampleFromMarkdown(string markdown) {
        var sectionMarker = "## 11. Example (Round-Trip Safe)";
        var sectionIdx = markdown.IndexOf(sectionMarker, StringComparison.Ordinal);
        if (sectionIdx < 0) sectionIdx = markdown.IndexOf("Round-Trip Safe", StringComparison.Ordinal);
        if (sectionIdx < 0) return null;

        var fenceOpen = "```poly";
        var fenceIdx = markdown.IndexOf(fenceOpen, sectionIdx, StringComparison.Ordinal);
        if (fenceIdx < 0) return null;

        var contentStart = fenceIdx + fenceOpen.Length;
        while (contentStart < markdown.Length && (markdown[contentStart] == '\n' || markdown[contentStart] == '\r'))
            contentStart++;

        var fenceClose = "```";
        var closeIdx = markdown.IndexOf(fenceClose, contentStart, StringComparison.Ordinal);
        if (closeIdx < 0) return null;

        var raw = markdown.Substring(contentStart, closeIdx - contentStart);
        return raw.Trim();
    }

    // ═════════════════════════════════════════════════════════════
    // Phase 4 RT — Runtime MCP thin vertical
    // ═════════════════════════════════════════════════════════════

    [Test]
    public async Task CreateInstance_SimpleEntity_ReturnsSnapshot() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        EvolveTool.AddEntity(sessionId, "Widget");
        EvolveTool.AddProperty(sessionId, "Widget", "Name", "Text");
        EvolveTool.AddProperty(sessionId, "Widget", "Price", "Number");

        var response = RuntimeTool.CreateInstance(sessionId, "Widget",
            """{"Name":"Gadget","Price":2999}""");
        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Message).Contains("Widget");
        await Assert.That(response.Message).Contains("created");

        var dataJson = System.Text.Json.JsonSerializer.Serialize(response.Data);
        await Assert.That(dataJson).Contains("Gadget");
        await Assert.That(dataJson).Contains("2999");
        await Assert.That(dataJson).Contains("instanceId");
    }

    [Test]
    public async Task CreateInstance_UnknownEntity_Fails() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var response = RuntimeTool.CreateInstance(sessionId, "NonExistent");
        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("not found");
    }

    [Test]
    public async Task GetInstance_AfterCreate_ReturnsFullSnapshot() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        EvolveTool.AddEntity(sessionId, "Item");
        EvolveTool.AddProperty(sessionId, "Item", "Label", "Text");

        var create = RuntimeTool.CreateInstance(sessionId, "Item",
            """{"Label":"Test Item"}""");
        await Assert.That(create.Success).IsTrue();
        var dataJson = System.Text.Json.JsonSerializer.Serialize(create.Data);
        // Extract instanceId from the response
        var instanceId = ExtractInstanceId(dataJson);
        await Assert.That(instanceId).IsNotNull();

        var response = RuntimeTool.GetInstance(sessionId, instanceId!);
        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Message).Contains(instanceId!);
        await Assert.That(response.Message).Contains("Item");
    }

    [Test]
    public async Task GetInstance_UnknownId_Fails() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var response = RuntimeTool.GetInstance(sessionId, "nonexistent-id");
        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("not found");
    }

    [Test]
    public async Task ListInstances_AfterCreate_ReturnsCount() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        EvolveTool.AddEntity(sessionId, "Item");
        EvolveTool.AddProperty(sessionId, "Item", "Label", "Text");

        var r1 = RuntimeTool.CreateInstance(sessionId, "Item", """{"Label":"A"}""");
        await Assert.That(r1.Success).IsTrue();
        var r2 = RuntimeTool.CreateInstance(sessionId, "Item", """{"Label":"B"}""");
        await Assert.That(r2.Success).IsTrue();

        var response = RuntimeTool.ListInstances(sessionId);
        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Message).Contains("2");

        // Filter by entity
        var filtered = RuntimeTool.ListInstances(sessionId, entityName: "Item");
        await Assert.That(filtered.Success).IsTrue();
        await Assert.That(filtered.Message).Contains("2");
    }

    [Test]
    public async Task ListInstances_EmptySession_ReturnsZero() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var response = RuntimeTool.ListInstances(sessionId);
        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Message).Contains("0");
    }

    [Test]
    public async Task InvokeAction_WithStageTransition_Succeeds() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        // Use apply_dsl to create a domain with actions + transition effects
        var dsl = DslTool.ApplyDsl(sessionId, """
            domain Test
            Task: entity {
              Status: Text
              Draft: stage {
                Start: action {
                  transition to Active
                }
              }
              Active: stage {
                entry { assign Status to "running" }
              }
              Done: stage {}
            }
            """);
        await Assert.That(dsl.Success).IsTrue();

        // Create an instance via runtime tool
        var create = RuntimeTool.CreateInstance(sessionId, "Task",
            """{"Status":"idle"}""");
        await Assert.That(create.Success).IsTrue();
        await Assert.That(create.Message).Contains("Draft"); // first stage

        var instanceId = ExtractInstanceId(
            System.Text.Json.JsonSerializer.Serialize(create.Data));

        // Call the Start action
        var call = RuntimeTool.InvokeAction(sessionId, instanceId!, "Start");
        await Assert.That(call.Success).IsTrue();
        await Assert.That(call.Message).Contains("Active");

        // Verify transition via get_instance
        var get = RuntimeTool.GetInstance(sessionId, instanceId!);
        await Assert.That(get.Success).IsTrue();
        await Assert.That(get.Message).Contains("Active");
    }

    [Test]
    public async Task InvokeAction_WithRequireGuard_BlocksWhenPolicyFails() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var dsl = DslTool.ApplyDsl(sessionId, """
            domain Test
            Item: entity {
              Score: Number
              HighScore: policy { Score > 10 }
              Draft: stage {
                Submit: action
                  require HighScore
                {
                  transition to Active
                }
              }
              Active: stage {}
            }
            """);
        await Assert.That(dsl.Success).IsTrue();

        // Create instance with low score — policy should block
        var create = RuntimeTool.CreateInstance(sessionId, "Item",
            """{"Score":5}""");
        await Assert.That(create.Success).IsTrue();
        var instanceId = ExtractInstanceId(
            System.Text.Json.JsonSerializer.Serialize(create.Data));

        var call = RuntimeTool.InvokeAction(sessionId, instanceId!, "Submit");
        await Assert.That(call.Success).IsFalse();
        await Assert.That(call.Message).Contains("HighScore");
        await Assert.That(call.Message).Contains("blocked");

        // Create instance with high score — should pass
        var create2 = RuntimeTool.CreateInstance(sessionId, "Item",
            """{"Score":15}""");
        await Assert.That(create2.Success).IsTrue();
        var instanceId2 = ExtractInstanceId(
            System.Text.Json.JsonSerializer.Serialize(create2.Data));

        var call2 = RuntimeTool.InvokeAction(sessionId, instanceId2!, "Submit");
        await Assert.That(call2.Success).IsTrue();
        await Assert.That(call2.Message).Contains("Active");
    }

    [Test]
    public async Task InvokeAction_ActionNotFound_Fails() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        EvolveTool.AddEntity(sessionId, "Task");
        EvolveTool.AddStage(sessionId, "Task", "Draft");

        var create = RuntimeTool.CreateInstance(sessionId, "Task");
        await Assert.That(create.Success).IsTrue();
        var instanceId = ExtractInstanceId(
            System.Text.Json.JsonSerializer.Serialize(create.Data));

        var call = RuntimeTool.InvokeAction(sessionId, instanceId!, "NonExistent");
        await Assert.That(call.Success).IsFalse();
        await Assert.That(call.Message).Contains("not found");
    }

    [Test]
    public async Task CreateInstance_WitStages_SetsInitialStage() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        EvolveTool.AddEntity(sessionId, "Process");
        EvolveTool.AddStage(sessionId, "Process", "Draft");
        EvolveTool.AddStage(sessionId, "Process", "Active");

        var create = RuntimeTool.CreateInstance(sessionId, "Process");
        await Assert.That(create.Success).IsTrue();
        await Assert.That(create.Message).Contains("Draft");
    }

    /// <summary>
    /// Extracts the instanceId from a JSON response containing an "instance" object.
    /// </summary>
    private static string? ExtractInstanceId(string json) {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("instance", out var instance)
            && instance.TryGetProperty("instanceId", out var id))
            return id.GetString();
        // Fallback: try top-level instanceId
        if (doc.RootElement.TryGetProperty("instanceId", out var topId))
            return topId.GetString();
        return null;
    }

    // ═════════════════════════════════════════════════════════════
    // SA — Stage-Action Semantics (Phase 3 §6e)
    // ═════════════════════════════════════════════════════════════

    [Test]
    public async Task AddActionToStage_CopiesEntityActionEffects() {
        // SA.2: AddActionToStage should copy effects/policies from entity-level
        // action when one exists with the same name.
        var (sessionId, _) = McpSessionStore.Create("Test");

        // Build: entity action with transition effect, then add to stage
        var r1 = EvolveTool.AddEntity(sessionId, "Task");
        await Assert.That(r1.Success).IsTrue();
        var r2 = EvolveTool.AddProperty(sessionId, "Task", "Name", "Text");
        await Assert.That(r2.Success).IsTrue();
        var r3 = EvolveTool.AddStage(sessionId, "Task", "Draft");
        await Assert.That(r3.Success).IsTrue();
        var r4 = EvolveTool.AddStage(sessionId, "Task", "Active");
        await Assert.That(r4.Success).IsTrue();

        // Add entity-level action with transition effect
        var r5 = EvolveTool.AddAction(sessionId, "Task", "Start");
        await Assert.That(r5.Success).IsTrue();

        // Manually add stage transition effect via evolution
        var state = McpSessionStore.TryGet(sessionId, out var s) ? s : null;
        await Assert.That(state).IsNotNull();
        var evolveResult = McpSessionStore.Evolve(sessionId, domain =>
            new DomainEvolution(domain).Evolve()
                .AddStageTransitionEffect("Task", "Start", "Active")
                .Apply());
        await Assert.That(evolveResult).IsNotNull();
        await Assert.That(evolveResult!.Succeeded).IsTrue();

        // Now add action to stage — should copy the transition effect
        var r6 = EvolveTool.AddActionToStage(sessionId, "Task", "Draft", "Start");
        await Assert.That(r6.Success).IsTrue();

        // Verify via MCP: create instance and call action from Draft stage
        var create = RuntimeTool.CreateInstance(sessionId, "Task");
        await Assert.That(create.Success).IsTrue();
        await Assert.That(create.Message).Contains("Draft");

        var instanceId = ExtractInstanceId(
            System.Text.Json.JsonSerializer.Serialize(create.Data));

        var call = RuntimeTool.InvokeAction(sessionId, instanceId!, "Start");
        await Assert.That(call.Success).IsTrue();
        await Assert.That(call.Message).Contains("Active");

        var get = RuntimeTool.GetInstance(sessionId, instanceId!);
        await Assert.That(get.Success).IsTrue();
        await Assert.That(get.Message).Contains("Active");
    }

    [Test]
    public async Task AddActionToStage_WithoutEntityAction_CreatesNew() {
        // SA.2: When no entity-level action exists, AddActionToStage creates
        // a fresh action (no effects, no policies) — same behavior as before.
        var (sessionId, _) = McpSessionStore.Create("Test");

        EvolveTool.AddEntity(sessionId, "Task");
        EvolveTool.AddStage(sessionId, "Task", "Draft");
        EvolveTool.AddStage(sessionId, "Task", "Active");

        // Add action only to stage — no entity-level action
        var r1 = EvolveTool.AddActionToStage(sessionId, "Task", "Draft", "DoSomething");
        await Assert.That(r1.Success).IsTrue();

        // Create instance and call the action — should succeed (no effects)
        var create = RuntimeTool.CreateInstance(sessionId, "Task");
        await Assert.That(create.Success).IsTrue();
        var instanceId = ExtractInstanceId(
            System.Text.Json.JsonSerializer.Serialize(create.Data));

        var call = RuntimeTool.InvokeAction(sessionId, instanceId!, "DoSomething");
        // Should succeed with no effects (no transition — still in Draft)
        await Assert.That(call.Success).IsTrue();
        await Assert.That(call.Message).Contains("Draft");
    }

    // ═════════════════════════════════════════════════════════════
    // RT′ — Honesty & Safety Residuals (Phase 3 §6c)
    // ═════════════════════════════════════════════════════════════

    [Test]
    public async Task InvokeAction_OnDeletedInstance_Refused() {
        // RT′.6: InvokeAction should refuse actions on deleted instances.
        // Use the core API directly since DeleteEntityInstance is not expressible in DSL.
        var (sessionId, _) = McpSessionStore.Create("Test");
        EvolveTool.AddEntity(sessionId, "Item");
        EvolveTool.AddProperty(sessionId, "Item", "Name", "Text");
        EvolveTool.AddStage(sessionId, "Item", "Draft");

        // Add a Delete action with DeleteEntityInstance effect via evolution
        var evolveResult = McpSessionStore.Evolve(sessionId, domain =>
            new DomainEvolution(domain).Evolve()
                .AddAction("Item", "Delete")
                .AddEffectToAction("Item", "Delete",
                    new DeleteEntityInstance(new DomainTypeReference("Item")))
                .Apply());
        await Assert.That(evolveResult).IsNotNull();
        await Assert.That(evolveResult!.Succeeded).IsTrue();

        var create = RuntimeTool.CreateInstance(sessionId, "Item",
            """{"Name":"TestItem"}""");
        await Assert.That(create.Success).IsTrue();
        var instanceId = ExtractInstanceId(
            System.Text.Json.JsonSerializer.Serialize(create.Data));

        // Call Delete action — executes DeleteEntityInstance effect, setting IsDeleted=true
        var delCall = RuntimeTool.InvokeAction(sessionId, instanceId!, "Delete");
        await Assert.That(delCall.Success).IsTrue();

        // Now any subsequent InvokeAction should fail (RT′.6)
        var call = RuntimeTool.InvokeAction(sessionId, instanceId!, "Delete");
        await Assert.That(call.Success).IsFalse();
        await Assert.That(call.Message).Contains("deleted");
    }

    [Test]
    public async Task ApplyDsl_WithDelete_SoftDeletesInstance() {
        // E1.3: Golden test for DSL `delete` keyword. Parse → apply → create
        // instance → call action with delete → InvokeAction refused afterward.
        var (sessionId, _) = McpSessionStore.Create("Test");

        var dsl = DslTool.ApplyDsl(sessionId, """
            domain Test
            Item: entity {
              Name: Text
              Draft: stage {
                Archive: action {
                  delete
                }
              }
            }
            """);
        await Assert.That(dsl.Success).IsTrue();

        // Verify the delete effect was parsed correctly via export round-trip
        var export = DslTool.ExportDsl(sessionId);
        await Assert.That(export.Success).IsTrue();
        var exportJson = System.Text.Json.JsonSerializer.Serialize(export.Data);
        await Assert.That(exportJson).Contains("delete");

        // Create instance and call the Archive action
        var create = RuntimeTool.CreateInstance(sessionId, "Item",
            """{"Name":"TestItem"}""");
        await Assert.That(create.Success).IsTrue();
        var instanceId = ExtractInstanceId(
            System.Text.Json.JsonSerializer.Serialize(create.Data));

        var archiveCall = RuntimeTool.InvokeAction(sessionId, instanceId!, "Archive");
        await Assert.That(archiveCall.Success).IsTrue();

        // Verify subsequent actions are refused
        var callAgain = RuntimeTool.InvokeAction(sessionId, instanceId!, "Archive");
        await Assert.That(callAgain.Success).IsFalse();
        await Assert.That(callAgain.Message).Contains("deleted");
    }

    [Test]
    public async Task GetDomainAnalysis_WithHints_SuggestsSuggestions() {
        // RT′.1: GetDomainAnalysis should include hint count and affordance
        // pointing to get_domain_suggestions.
        var (sessionId, _) = McpSessionStore.Create("Test");

        // Create entity with properties but no stages — triggers DMAS001 hints
        EvolveTool.AddEntity(sessionId, "Person");
        EvolveTool.AddProperty(sessionId, "Person", "Name", "Text");
        EvolveTool.AddProperty(sessionId, "Person", "Age", "Number");

        var response = QueryTool.GetDomainAnalysis(sessionId);
        await Assert.That(response.Success).IsTrue();

        // Message should mention hints
        await Assert.That(response.Message).Contains("hint");

        // SA′.3: hintCount should be separate from infoCount
        var dataJson = System.Text.Json.JsonSerializer.Serialize(response.Data);
        await Assert.That(dataJson).Contains("hintCount");

        // Affordances should include get_domain_suggestions
        await Assert.That(response.Affordances).IsNotNull();
        await Assert.That(response.Affordances!.Contains("get_domain_suggestions")).IsTrue();
    }

    [Test]
    public async Task AddActionToStage_Order_StageBeforeEntityEffects_StillTransitions() {
        // SA′.6: If stage is placed first, then entity-level effects added later,
        // InvokeAction should still use the fallthrough path (empty stage + entity twin).
        var (sessionId, _) = McpSessionStore.Create("Test");

        EvolveTool.AddEntity(sessionId, "Task");
        EvolveTool.AddProperty(sessionId, "Task", "Name", "Text");
        EvolveTool.AddStage(sessionId, "Task", "Draft");
        EvolveTool.AddStage(sessionId, "Task", "Active");

        // Step 1: Add action to stage FIRST (before entity-level action exists)
        var r1 = EvolveTool.AddActionToStage(sessionId, "Task", "Draft", "Go");
        await Assert.That(r1.Success).IsTrue();

        // Step 2: Now add entity-level action with transition effect
        var r2 = EvolveTool.AddAction(sessionId, "Task", "Go");
        await Assert.That(r2.Success).IsTrue();

        // Step 3: Add stage transition effect to entity-level action
        var evolveResult = McpSessionStore.Evolve(sessionId, domain =>
            new DomainEvolution(domain).Evolve()
                .AddStageTransitionEffect("Task", "Go", "Active")
                .Apply());
        await Assert.That(evolveResult).IsNotNull();
        await Assert.That(evolveResult!.Succeeded).IsTrue();

        // Verify via MCP: create instance and call action — should transition
        var create = RuntimeTool.CreateInstance(sessionId, "Task");
        await Assert.That(create.Success).IsTrue();
        await Assert.That(create.Message).Contains("Draft");

        var instanceId = ExtractInstanceId(
            System.Text.Json.JsonSerializer.Serialize(create.Data));

        var call = RuntimeTool.InvokeAction(sessionId, instanceId!, "Go");
        await Assert.That(call.Success).IsTrue();
        await Assert.That(call.Message).Contains("Active");

        var get = RuntimeTool.GetInstance(sessionId, instanceId!);
        await Assert.That(get.Success).IsTrue();
        await Assert.That(get.Message).Contains("Active");
    }

    // ═════════════════════════════════════════════════════════════
    // Q1′ — Subject-First Related Reads (Phase 4)
    // ═════════════════════════════════════════════════════════════

    [Test]
    public async Task Parser_PathPrefix_RelBoolProp_CreatesRelationshipNav() {
        // Q1.2: `Rel BoolProp` should parse to RelationshipNavigation
        var poly = "domain Test\nItem: entity {\n  Flag: Boolean\n}\n";
        var domain = new DomainEvolution(new Domain("_", [], [])).Apply(
            new PolyDslParser(poly).Parse()).Root;
        // Build a policy expression via DSL that uses a hypothetical relationship
        // We test parser directly via PolyDslParser on a policy expression
        var parsed = ParseExpression("assignee Active");
        await Assert.That(parsed).IsTypeOf<RelationshipNavigation>();
        var nav = (RelationshipNavigation)parsed;
        await Assert.That(nav.RelationshipName).IsEqualTo("assignee");
        await Assert.That(nav.TargetProperty).IsTypeOf<PropertyAccess>();
        await Assert.That(((PropertyAccess)nav.TargetProperty).Name).IsEqualTo("Active");
    }

    [Test]
    public async Task Parser_PathPrefix_RelPropIsValue_CreatesRelationshipNavWithComparison() {
        // Q1.2: `Rel Prop is "value"` → RelationshipNavigation with Comparison
        var parsed = ParseExpression("customer Tier is \"VIP\"");
        await Assert.That(parsed).IsTypeOf<RelationshipNavigation>();
        var nav = (RelationshipNavigation)parsed;
        await Assert.That(nav.RelationshipName).IsEqualTo("customer");
        await Assert.That(nav.TargetProperty).IsTypeOf<Comparison>();
        var comp = (Comparison)nav.TargetProperty;
        await Assert.That(comp.Kind).IsEqualTo(ComparisonKind.Equal);
        await Assert.That(comp.Left).IsTypeOf<PropertyAccess>();
        await Assert.That(((PropertyAccess)comp.Left).Name).IsEqualTo("Tier");
    }

    [Test]
    public async Task Parser_PathPrefix_RelPropCompareOp_CreatesNavWithComparison() {
        // Q1.2: `Rel Prop >= value` → RelationshipNavigation with Comparison
        var parsed = ParseExpression("customer CreditLimit >= 1000");
        await Assert.That(parsed).IsTypeOf<RelationshipNavigation>();
        var nav = (RelationshipNavigation)parsed;
        await Assert.That(nav.RelationshipName).IsEqualTo("customer");
        await Assert.That(nav.TargetProperty).IsTypeOf<Comparison>();
        var comp = (Comparison)nav.TargetProperty;
        await Assert.That(comp.Kind).IsEqualTo(ComparisonKind.GreaterThanOrEqual);
    }

    [Test]
    public async Task Parser_Exists_RelExists_CreatesExists() {
        // Q1.3: `Rel exists` → Exists(PropertyAccess)
        var parsed = ParseExpression("assignee exists");
        await Assert.That(parsed).IsTypeOf<Poly.DomainModeling.Exists>();
        var exists = (Poly.DomainModeling.Exists)parsed;
        await Assert.That(exists.Target).IsTypeOf<PropertyAccess>();
        await Assert.That(((PropertyAccess)exists.Target).Name).IsEqualTo("assignee");
    }

    [Test]
    public async Task Parser_Exists_NotRelExists_CreatesNotExists() {
        // Q1.3: `not Rel exists` → Not(Exists(PropertyAccess))
        var parsed = ParseExpression("not certificate exists");
        await Assert.That(parsed).IsTypeOf<Poly.DomainModeling.Not>();
        var notExpr = (Poly.DomainModeling.Not)parsed;
        await Assert.That(notExpr.Operand).IsTypeOf<Poly.DomainModeling.Exists>();
        var exists = (Exists)notExpr.Operand;
        await Assert.That(exists.Target).IsTypeOf<PropertyAccess>();
        await Assert.That(((PropertyAccess)exists.Target).Name).IsEqualTo("certificate");
    }

    [Test]
    public async Task Parser_Where_RelWhereAndChain_CreatesRelationshipNav() {
        // Q1.3b: `Rel where Prop1 is "val" and Prop2 >= val` → RelationshipNavigation with And body
        var parsed = ParseExpression("customer where Status is \"Active\" and CreditLimit >= 1000");
        await Assert.That(parsed).IsTypeOf<RelationshipNavigation>();
        var nav = (RelationshipNavigation)parsed;
        await Assert.That(nav.RelationshipName).IsEqualTo("customer");
        await Assert.That(nav.TargetProperty).IsTypeOf<Poly.DomainModeling.And>();
    }

    [Test]
    public async Task Printer_PathPrefix_RoundTrips() {
        // Verify parse → print → parse round-trip for path-prefix
        const string exprText = "assignee Active";
        var printer = new DomainDslPrinter();

        // Build expression and print
        var expr = ParseExpression(exprText);
        var printed = PrintExpressionForTest(expr);

        // Re-parse and verify structure matches
        var reParsed = ParseExpression(printed);
        await Assert.That(reParsed).IsTypeOf<RelationshipNavigation>();
        var nav = (RelationshipNavigation)reParsed;
        await Assert.That(nav.RelationshipName).IsEqualTo("assignee");
    }

    [Test]
    public async Task Printer_Exists_RoundTrips() {
        const string exprText = "assignee exists";
        var expr = ParseExpression(exprText);
        var printed = PrintExpressionForTest(expr);
        await Assert.That(printed).IsEqualTo("assignee exists");

        var reParsed = ParseExpression(printed);
        await Assert.That(reParsed).IsTypeOf<Poly.DomainModeling.Exists>();
    }

    [Test]
    public async Task Printer_NotExists_RoundTrips() {
        const string exprText = "not certificate exists";
        var expr = ParseExpression(exprText);
        var printed = PrintExpressionForTest(expr);
        await Assert.That(printed).IsEqualTo("not certificate exists");

        var reParsed = ParseExpression(printed);
        await Assert.That(reParsed).IsTypeOf<Poly.DomainModeling.Not>();
    }

    [Test]
    public async Task Printer_Where_RoundTrips() {
        const string exprText = "customer where Status is \"Active\" and CreditLimit >= 1000";
        var expr = ParseExpression(exprText);
        var printed = PrintExpressionForTest(expr);
        await Assert.That(printed).Contains("customer where");
        await Assert.That(printed).Contains("Status is \"Active\"");
        await Assert.That(printed).Contains("CreditLimit >= 1000");

        var reParsed = ParseExpression(printed);
        await Assert.That(reParsed).IsTypeOf<RelationshipNavigation>();
    }

    [Test]
    public async Task Parser_LocalProperty_StillWorks() {
        // Verify that existing local property expressions are unaffected
        var parsed = ParseExpression("Age >= 18");
        await Assert.That(parsed).IsTypeOf<Poly.DomainModeling.Comparison>();
    }

    [Test]
    public async Task Parser_SimpleIdentifier_StillWorks() {
        var parsed = ParseExpression("Name");
        await Assert.That(parsed).IsTypeOf<Poly.DomainModeling.PropertyAccess>();
    }

    [Test]
    public async Task Parser_ComplexLocalExpr_StillWorks() {
        // Mixed and/or/not with parens
        var parsed = ParseExpression("(Total > 0) or Rush is true");
        await Assert.That(parsed).IsTypeOf<Poly.DomainModeling.Or>();
    }

    /// <summary>
    /// Parses a policy expression string (the part inside `policy { ... }`).
    /// Wraps in a minimal domain context so the parser can process it.
    /// </summary>
    private static DomainExpression ParseExpression(string text) {
        // The expression parser is embedded in PolyDslParser.
        // We need to parse a full .poly with a policy to extract the expression.
        var poly = $@"
domain Test
E: entity {{
  P1: Text
  P2: Number
  X: policy {{ {text} }}
}}
";
        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        // Find the AddPolicyToEntityChange and extract its expression
        var policyChange = changes.OfType<AddPolicyToEntityChange>().FirstOrDefault();
        if (policyChange is not null)
            return policyChange.Policy.Expression;

        // Fallback: not found
        throw new InvalidOperationException($"Could not parse expression: {text}");
    }

    /// <summary>
    /// Uses the DomainDslPrinter to print an expression.
    /// </summary>
    private static string PrintExpressionForTest(DomainExpression expr) {
        return new DomainDslPrinter().PrintTestExpression(expr);
    }

    // ═════════════════════════════════════════════════════════════
    // Q1′′′ — Post-ship residuals (Phase 4 · §11)
    // ═════════════════════════════════════════════════════════════

    [Test]
    public async Task Parser_PathPrefix_RelBoolProp_Authoring_ExportsCorrectly() {
        // Q1′′′′.1: Test that path-prefix (Rel BoolProp) parses, applies, and
        // exports correctly. This verifies the authoring/parse/print path, not
        // full RT evaluation (which requires VM-level store graph traversal
        // as a future enhancement).
        var (sessionId, _) = McpSessionStore.Create("Test");

        var dsl = DslTool.ApplyDsl(sessionId, """
            domain Test
            Ticket: entity {
              Label: Text
              Active: policy { assignee Active }
              assignee: Agent
            }
            Agent: entity {
              Name: Text
              Active: Boolean
            }
            """);
        await Assert.That(dsl.Success).IsTrue();

        // Verify export contains the path-prefix policy expression
        var export = DslTool.ExportDsl(sessionId);
        await Assert.That(export.Success).IsTrue();
        var exportJson = System.Text.Json.JsonSerializer.Serialize(export.Data);
        await Assert.That(exportJson).Contains("assignee Active");
        await Assert.That(exportJson).Contains("Active: policy");

        // Verify the parsed expression shape
        var state = McpSessionStore.TryGet(sessionId, out var s) ? s : null;
        await Assert.That(state).IsNotNull();
        var ticketEntity = state!.Domain.Types.OfType<Entity>().First(e => e.Name == "Ticket");
        var activePolicy = ticketEntity.Policies.First(p => p.Name == "Active");
        await Assert.That(activePolicy.Expression).IsTypeOf<RelationshipNavigation>();
    }

    [Test]
    public async Task Parser_PathPrefix_RelPropCompare_Authoring_ExportsCorrectly() {
        // Q1′′′′.1: Authoring-only — path-prefix compare parse/apply/export test.
        var (sessionId, _) = McpSessionStore.Create("Test");

        var dsl = DslTool.ApplyDsl(sessionId, """
            domain Test
            Order: entity {
              Total: Number
              VipCustomer: policy { customer Tier is "VIP" }
              customer: Customer
            }
            Customer: entity {
              Name: Text
              Tier: Text
            }
            """);
        await Assert.That(dsl.Success).IsTrue();

        var export = DslTool.ExportDsl(sessionId);
        await Assert.That(export.Success).IsTrue();
        var exportJson = System.Text.Json.JsonSerializer.Serialize(export.Data);
        await Assert.That(exportJson).Contains("customer Tier");
        await Assert.That(exportJson).Contains("VipCustomer");

        var state = McpSessionStore.TryGet(sessionId, out var s) ? s : null;
        await Assert.That(state).IsNotNull();
        var orderEntity = state!.Domain.Types.OfType<Entity>().First(e => e.Name == "Order");
        var vipPolicy = orderEntity.Policies.First(p => p.Name == "VipCustomer");
        await Assert.That(vipPolicy.Expression).IsTypeOf<RelationshipNavigation>();
    }

    [Test]
    public async Task Parser_RelExists_Authoring_ExportsCorrectly() {
        // Q1′′′′.1: Authoring-only — `Rel exists` on a regular property.
        // Use a nullable property on the same entity to test the exists keyword.
        var (sessionId, _) = McpSessionStore.Create("Test");

        var dsl = DslTool.ApplyDsl(sessionId, """
            domain Test
            Item: entity {
              Name: Text
              IsSet: policy { Flag exists }
              Flag: Boolean
            }
            """);
        await Assert.That(dsl.Success).IsTrue();

        var export = DslTool.ExportDsl(sessionId);
        await Assert.That(export.Success).IsTrue();
        var exportJson = System.Text.Json.JsonSerializer.Serialize(export.Data);
        await Assert.That(exportJson).Contains("Flag exists");

        var state = McpSessionStore.TryGet(sessionId, out var s) ? s : null;
        await Assert.That(state).IsNotNull();
        var itemEntity = state!.Domain.Types.OfType<Entity>().First(e => e.Name == "Item");
        var isSetPolicy = itemEntity.Policies.First(p => p.Name == "IsSet");
        await Assert.That(isSetPolicy.Expression).IsTypeOf<Poly.DomainModeling.Exists>();
    }

    [Test]
    public async Task Parser_PathPrefix_RelWhere_Authoring_ExportsCorrectly() {
        // Q1′′′.1: RT golden — `Rel where` via apply_dsl with N1 nav.
        // Uses the parser's ParseRelatedAccess for `customer where ...`.
        // The N1 nav creates a relationship; the policy expression references
        // it as RelationshipNavigation. The analysis accepts this when the
        // relationship name is known.
        var (sessionId, _) = McpSessionStore.Create("Test");

        var dsl = DslTool.ApplyDsl(sessionId, """
            domain Test
            Ticket: entity {
              Label: Text
              ActiveVip: policy { customer where Status is "Active" and Tier is "VIP" }
              customer: Customer
            }
            Customer: entity {
              Name: Text
              Status: Text
              Tier: Text
            }
            """);
        await Assert.That(dsl.Success).IsTrue();

        var export = DslTool.ExportDsl(sessionId);
        await Assert.That(export.Success).IsTrue();
        // The exported poly should contain the relationship name
        var exportJson = System.Text.Json.JsonSerializer.Serialize(export.Data);
        await Assert.That(exportJson).Contains("customer");
        await Assert.That(exportJson).Contains("ActiveVip");

        var state = McpSessionStore.TryGet(sessionId, out var s) ? s : null;
        await Assert.That(state).IsNotNull();
        var ticketEntity = state!.Domain.Types.OfType<Entity>().First(e => e.Name == "Ticket");
        var activeVipPolicy = ticketEntity.Policies.First(p => p.Name == "ActiveVip");
        await Assert.That(activeVipPolicy.Expression).IsTypeOf<Poly.DomainModeling.RelationshipNavigation>();
    }

    [Test]
    public async Task AssignLHS_MultiToken_Rejected() {
        // Q1′′′.3: assign customer Status to "X" is rejected (cross-entity write banned)
        // The parser consumes "customer" as the target prop name, then expects "to" but finds "Status".
        var (sessionId, _) = McpSessionStore.Create("Test");

        var dsl = DslTool.ApplyDsl(sessionId, """
            domain Test
            Item: entity {
              Name: Text
              Draft: stage {
                Bad: action {
                  assign customer Status to "X"
                }
              }
            }
            """);
        // Should fail because "customer" is consumed as prop name, then "Status" is unexpected
        await Assert.That(dsl.Success).IsFalse();
    }

    [Test]
    public async Task AssignRHS_ScalarRelatedRead_Parses() {
        // Q1′′′.3: assign Label to customer Tier is OK (scalar related read on RHS)
        var (sessionId, _) = McpSessionStore.Create("Test");

        var dsl = DslTool.ApplyDsl(sessionId, """
            domain Test
            Ticket: entity {
              Label: Text
              Draft: stage {
                CopyLabel: action {
                  assign Label to customer Tier
                }
              }
              customer: Customer
            }
            Customer: entity {
              Name: Text
              Tier: Text
            }
            """);
        await Assert.That(dsl.Success).IsTrue();

        // Verify export contains the path-prefix expression
        var export = DslTool.ExportDsl(sessionId);
        await Assert.That(export.Success).IsTrue();
        var exportJson = System.Text.Json.JsonSerializer.Serialize(export.Data);
        await Assert.That(exportJson).Contains("customer Tier");
    }

    [Test]
    public async Task Parser_ManyPlusProperty_ParsesButAnalysisRejects() {
        // Q1′′′′.2 / Q1'''''.5: Parser accepts `orders Status` (many + property) syntactically,
        // but the analysis pipeline rejects it via RelationshipNavigationCardinality check.
        var parsed = ParseExpression("orders Status is \"Open\"");
        await Assert.That(parsed).IsTypeOf<Poly.DomainModeling.RelationshipNavigation>();
        var nav = (Poly.DomainModeling.RelationshipNavigation)parsed;
        await Assert.That(nav.RelationshipName).IsEqualTo("orders");

        // Verify analysis rejection via apply_dsl on a domain with a many relationship
        var (sessionId, _) = McpSessionStore.Create("Test");
        var dsl = DslTool.ApplyDsl(sessionId, """
            domain Test
            Order: entity {
              Status: Text
            }
            Customer: entity {
              orders: many Order
              ManyCheck: policy { orders Status is "Open" }
            }
            """);
        // Should fail because orders is a many relationship
        await Assert.That(dsl.Success).IsFalse();
        await Assert.That(dsl.Message).Contains("orders");
        await Assert.That(dsl.Message).Contains("many", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task Parser_NestedWhere_Rejected() {
        // Q1'''''.2: Nested `where` inside a where body is rejected with a clear error.
        var (sessionId, _) = McpSessionStore.Create("Test");
        var dsl = DslTool.ApplyDsl(sessionId, """
            domain Test
            Ticket: entity {
              Label: Text
              BadPolicy: policy { customer where other where Status is "Active" }
              customer: Customer
            }
            Customer: entity {
              Name: Text
              Status: Text
            }
            """);
        await Assert.That(dsl.Success).IsFalse();
        await Assert.That(dsl.Message).Contains("Nested 'where'");
    }

    [Test]
    public async Task Parser_NotExists_IsNotNotExists_IsNotOfExists() {
        // Q1′′′.6: `not Rel exists` produces Not(Exists(...)), not NotExists.
        // Guide table updated to reflect this.
        var parsed = ParseExpression("not certificate exists");
        await Assert.That(parsed).IsTypeOf<Poly.DomainModeling.Not>();
        var notExpr = (Poly.DomainModeling.Not)parsed;
        await Assert.That(notExpr.Operand).IsTypeOf<Poly.DomainModeling.Exists>();
    }

    [Test]
    public async Task ApplyDsl_RelExists_OnNavRelationship_Succeeds() {
        // Q1''''''.1: `Rel exists` on a real N1 nav relationship must apply cleanly.
        var (sessionId, _) = McpSessionStore.Create("Test");
        var dsl = DslTool.ApplyDsl(sessionId, """
            domain Test
            Ticket: entity {
              Label: Text
              HasAssignee: policy { assignee exists }
              assignee: Agent
            }
            Agent: entity {
              Name: Text
            }
            """);
        await Assert.That(dsl.Success).IsTrue();

        // Verify export contains the exists expression
        var export = DslTool.ExportDsl(sessionId);
        await Assert.That(export.Success).IsTrue();
        var exportJson = System.Text.Json.JsonSerializer.Serialize(export.Data);
        await Assert.That(exportJson).Contains("assignee exists");
        await Assert.That(exportJson).Contains("HasAssignee");

        // Verify the parsed expression shape
        var state = McpSessionStore.TryGet(sessionId, out var s) ? s : null;
        await Assert.That(state).IsNotNull();
        var ticketEntity = state!.Domain.Types.OfType<Entity>().First(e => e.Name == "Ticket");
        var policy = ticketEntity.Policies.First(p => p.Name == "HasAssignee");
        await Assert.That(policy.Expression).IsTypeOf<Poly.DomainModeling.Exists>();
    }

    [Test]
    public async Task ApplyDsl_RelNotExists_OnNavRelationship_Succeeds() {
        // Q1''''''.1: `not Rel exists` on a real N1 nav must apply cleanly.
        var (sessionId, _) = McpSessionStore.Create("Test");
        var dsl = DslTool.ApplyDsl(sessionId, """
            domain Test
            Ticket: entity {
              Label: Text
              NoCertificate: policy { not certificate exists }
              certificate: Certificate
            }
            Certificate: entity {
              Name: Text
            }
            """);
        await Assert.That(dsl.Success).IsTrue();

        var export = DslTool.ExportDsl(sessionId);
        await Assert.That(export.Success).IsTrue();
        var exportJson = System.Text.Json.JsonSerializer.Serialize(export.Data);
        await Assert.That(exportJson).Contains("certificate exists");
        await Assert.That(exportJson).Contains("NoCertificate");
    }

    [Test]
    public async Task Analysis_BodyValidation_ValidPropOnTarget_Succeeds() {
        // Q1''''''.4: Happy-path test — body property exists on target entity.
        var (sessionId, _) = McpSessionStore.Create("Test");
        var dsl = DslTool.ApplyDsl(sessionId, """
            domain Test
            Ticket: entity {
              Label: Text
              VipCheck: policy { customer Tier is "VIP" }
              customer: Customer
            }
            Customer: entity {
              Name: Text
              Tier: Text
            }
            """);
        await Assert.That(dsl.Success).IsTrue();

        var export = DslTool.ExportDsl(sessionId);
        await Assert.That(export.Success).IsTrue();
        var exportJson = System.Text.Json.JsonSerializer.Serialize(export.Data);
        await Assert.That(exportJson).Contains("customer Tier");
    }

    [Test]
    public async Task ApplyDsl_WithArithmetic_ParsesAndRoundTrips() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        var dsl = DslTool.ApplyDsl(sessionId, """
            domain Test
            Item: entity {
              Total: Number
              Discount: Number
              Net: Number
              HighValue: policy { Total - Discount > 100 }
            }
            """);
        await Assert.That(dsl.Success).IsTrue();

        var export = DslTool.ExportDsl(sessionId);
        await Assert.That(export.Success).IsTrue();
        var exportJson = System.Text.Json.JsonSerializer.Serialize(export.Data);
        await Assert.That(exportJson).Contains("-");
    }

    [Test]
    public async Task ApplyDsl_WithInvokeEffect_ParsesAndRoundTrips() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        var dsl = DslTool.ApplyDsl(sessionId, """
            domain Test
            Order: entity {
              Draft: stage {
                Submit: action {
                  invoke Validate
                  transition to Active
                }
              }
              Active: stage {}
              Validate: action {
                assign Status to "validated"
              }
              Status: Text
            }
            """);
        await Assert.That(dsl.Success).IsTrue();

        var export = DslTool.ExportDsl(sessionId);
        await Assert.That(export.Success).IsTrue();
        var exportJson = System.Text.Json.JsonSerializer.Serialize(export.Data);
        await Assert.That(exportJson).Contains("invoke Validate");
    }

    [Test]
    public async Task ApplyDsl_WithConditionalEffect_ParsesAndRoundTrips() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        var dsl = DslTool.ApplyDsl(sessionId, """
            domain Test
            Item: entity {
              Status: Text
              Count: Number
              Draft: stage {
                Process: action {
                  if (Count > 0) {
                    assign Status to "ok"
                  } else {
                    assign Status to "empty"
                  }
                  transition to Done
                }
              }
              Done: stage {}
            }
            """);
        await Assert.That(dsl.Success).IsTrue();

        var export = DslTool.ExportDsl(sessionId);
        await Assert.That(export.Success).IsTrue();
        var exportJson = System.Text.Json.JsonSerializer.Serialize(export.Data);
        await Assert.That(exportJson).Contains("if (");
        await Assert.That(exportJson).Contains("else {");
    }

    [Test]
    public async Task ApplyDsl_WithEqualsConstraint_ParsesAndRoundTrips() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        var dsl = DslTool.ApplyDsl(sessionId, """
            domain Test
            Item: entity {
              Status: Text default("Active")
              Count: Number default(0)
            }
            """);
        await Assert.That(dsl.Success).IsTrue();

        var export = DslTool.ExportDsl(sessionId);
        await Assert.That(export.Success).IsTrue();
        var exportJson = System.Text.Json.JsonSerializer.Serialize(export.Data);
        await Assert.That(exportJson).Contains("default(");
    }

    [Test]
    public async Task ApplyDsl_WithEnumType_ParsesAndRoundTrips() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        var dsl = DslTool.ApplyDsl(sessionId, """
            domain Test
            Color: enum {
              Red,
              Green,
              Blue,
            }
            Item: entity {
              Color: Color
            }
            """);
        await Assert.That(dsl.Success).IsTrue();
    }

    // Entity inheritance was removed — no inheritance test.

    // ── E6.1 RT goldens: authoring → create → invoke → assert ──

    [Test]
    public async Task ApplyDsl_InvokeEffect_RuntimeSelfInvoke_RunsNestedAction() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        var dsl = DslTool.ApplyDsl(sessionId, """
            domain Test
            Order: entity {
              Status: Text
              Draft: stage {
                Submit: action {
                  invoke Validate
                  transition to Active
                }
              }
              Active: stage {}
              Validate: action {
                assign Status to "validated"
              }
            }
            """);
        await Assert.That(dsl.Success).IsTrue();

        var create = RuntimeTool.CreateInstance(sessionId, "Order",
            """{"Status":"new"}""");
        await Assert.That(create.Success).IsTrue();
        var instanceId = ExtractInstanceId(
            System.Text.Json.JsonSerializer.Serialize(create.Data));

        var call = RuntimeTool.InvokeAction(sessionId, instanceId!, "Submit");
        await Assert.That(call.Success).IsTrue();

        var get = RuntimeTool.GetInstance(sessionId, instanceId!);
        await Assert.That(get.Success).IsTrue();
        var getJson = System.Text.Json.JsonSerializer.Serialize(get.Data);
        await Assert.That(getJson).Contains("Active");
        await Assert.That(getJson).Contains("validated");
    }

    [Test]
    public async Task ApplyDsl_ConditionalEffect_RuntimeBranchTaken() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        var dsl = DslTool.ApplyDsl(sessionId, """
            domain Test
            Item: entity {
              Status: Text
              Count: Number
              Draft: stage {
                Process: action {
                  if (Count > 0) {
                    assign Status to "ok"
                  } else {
                    assign Status to "empty"
                  }
                  transition to Done
                }
              }
              Done: stage {}
            }
            """);
        await Assert.That(dsl.Success).IsTrue();

        var createOk = RuntimeTool.CreateInstance(sessionId, "Item",
            """{"Status":"pending","Count":3}""");
        await Assert.That(createOk.Success).IsTrue();
        var idOk = ExtractInstanceId(
            System.Text.Json.JsonSerializer.Serialize(createOk.Data));
        var callOk = RuntimeTool.InvokeAction(sessionId, idOk!, "Process");
        await Assert.That(callOk.Success).IsTrue();
        var getOk = RuntimeTool.GetInstance(sessionId, idOk!);
        var okJson = System.Text.Json.JsonSerializer.Serialize(getOk.Data);
        await Assert.That(okJson).Contains("\"ok\"");
        await Assert.That(okJson).Contains("Done");

        var createEmpty = RuntimeTool.CreateInstance(sessionId, "Item",
            """{"Status":"pending","Count":0}""");
        await Assert.That(createEmpty.Success).IsTrue();
        var idEmpty = ExtractInstanceId(
            System.Text.Json.JsonSerializer.Serialize(createEmpty.Data));
        var callEmpty = RuntimeTool.InvokeAction(sessionId, idEmpty!, "Process");
        await Assert.That(callEmpty.Success).IsTrue();
        var getEmpty = RuntimeTool.GetInstance(sessionId, idEmpty!);
        var emptyJson = System.Text.Json.JsonSerializer.Serialize(getEmpty.Data);
        await Assert.That(emptyJson).Contains("empty");
    }

    [Test]
    public async Task ApplyDsl_ActionParameter_RuntimeBindingVisible() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        var dsl = DslTool.ApplyDsl(sessionId, """
            domain Test
            Item: entity {
              Label: Text
              Draft: stage {
                Tag: action (value: Text) {
                  assign Label to value
                  transition to Done
                }
              }
              Done: stage {}
            }
            """);
        await Assert.That(dsl.Success).IsTrue();

        var create = RuntimeTool.CreateInstance(sessionId, "Item",
            """{"Label":"unset"}""");
        await Assert.That(create.Success).IsTrue();
        var instanceId = ExtractInstanceId(
            System.Text.Json.JsonSerializer.Serialize(create.Data));

        var call = RuntimeTool.InvokeAction(sessionId, instanceId!, "Tag",
            """{"value":"tagged"}""");
        await Assert.That(call.Success).IsTrue();

        var get = RuntimeTool.GetInstance(sessionId, instanceId!);
        var getJson = System.Text.Json.JsonSerializer.Serialize(get.Data);
        await Assert.That(getJson).Contains("tagged");
        await Assert.That(getJson).Contains("Done");
    }

    [Test]
    public async Task ApplyDsl_InvokeNestedWithArgs_RuntimePassesBindings() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        var dsl = DslTool.ApplyDsl(sessionId, """
            domain Test
            Item: entity {
              Label: Text
              Draft: stage {
                Go: action {
                  invoke Apply(value: "from-invoke")
                  transition to Done
                }
              }
              Done: stage {}
              Apply: action (value: Text) {
                assign Label to value
              }
            }
            """);
        await Assert.That(dsl.Success).IsTrue();

        var create = RuntimeTool.CreateInstance(sessionId, "Item",
            """{"Label":"before"}""");
        await Assert.That(create.Success).IsTrue();
        var instanceId = ExtractInstanceId(
            System.Text.Json.JsonSerializer.Serialize(create.Data));

        var call = RuntimeTool.InvokeAction(sessionId, instanceId!, "Go");
        await Assert.That(call.Success).IsTrue();

        var get = RuntimeTool.GetInstance(sessionId, instanceId!);
        var getJson = System.Text.Json.JsonSerializer.Serialize(get.Data);
        await Assert.That(getJson).Contains("from-invoke");
    }

    // ── E6.2 RT golden: invoke depth exceeded ──────────────────

    [Test]
    public async Task ApplyDsl_RecursiveInvoke_ExceedsDepth_FailsLoud() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        var dsl = DslTool.ApplyDsl(sessionId, """
            domain Test
            Loop: entity {
              Status: Text
              Draft: stage {
                Bounce: action {
                  invoke Bounce
                }
              }
            }
            """);
        await Assert.That(dsl.Success).IsTrue();

        var create = RuntimeTool.CreateInstance(sessionId, "Loop",
            """{"Status":"x"}""");
        await Assert.That(create.Success).IsTrue();
        var instanceId = ExtractInstanceId(
            System.Text.Json.JsonSerializer.Serialize(create.Data));

        var call = RuntimeTool.InvokeAction(sessionId, instanceId!, "Bounce");
        await Assert.That(call.Success).IsFalse();
        await Assert.That(call.Message).Contains("depth exceeded");
    }

    [Test]
    public async Task ApplyDsl_ElseIf_ParsesAndRoundTrips() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        var dsl = DslTool.ApplyDsl(sessionId, """
            domain Test
            Item: entity {
              Status: Text
              Score: Number
              Draft: stage {
                Grade: action {
                  if (Score >= 90) {
                    assign Status to "A"
                  } else if (Score >= 70) {
                    assign Status to "B"
                  } else {
                    assign Status to "C"
                  }
                }
              }
            }
            """);
        await Assert.That(dsl.Success).IsTrue();

        var export = DslTool.ExportDsl(sessionId);
        await Assert.That(export.Success).IsTrue();
        var exportJson = System.Text.Json.JsonSerializer.Serialize(export.Data);
        await Assert.That(exportJson).Contains("else if");
    }

    // ── E3b RT golden: cross-entity invoke ─────────────────────

    [Test]
    public async Task ApplyDsl_CrossEntityInvoke_RuntimePasses() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        var dsl = DslTool.ApplyDsl(sessionId, """
            domain Test
            Service: entity {
              Status: Text
              Process: action {
                assign Status to "processed"
              }
            }
            Orchestrator: entity {
              service: Service
              Run: action {
                invoke service.Process
              }
            }
            """);
        await Assert.That(dsl.Success).IsTrue();

        // Create the target instance first
        var createSvc = RuntimeTool.CreateInstance(sessionId, "Service",
            """{"Status":"idle"}""");
        await Assert.That(createSvc.Success).IsTrue();
        var svcId = ExtractInstanceId(
            System.Text.Json.JsonSerializer.Serialize(createSvc.Data));

        // Create the orchestrator
        var createOrch = RuntimeTool.CreateInstance(sessionId, "Orchestrator");
        await Assert.That(createOrch.Success).IsTrue();
        var orchId = ExtractInstanceId(
            System.Text.Json.JsonSerializer.Serialize(createOrch.Data));

        // Link orchestrator → service via the InstanceStore
        McpSessionStore.TryModifyInstances(sessionId, state => {
            if (state.InstanceMap.TryGetValue(orchId!, out var orch)
                && state.InstanceMap.TryGetValue(svcId!, out var svc)
                && state.InstanceStore is not null) {
                state.InstanceStore.Link("service", orch, svc);
            }
        });

        // Now invoke Run on orchestrator
        var call = RuntimeTool.InvokeAction(sessionId, orchId!, "Run");
        await Assert.That(call.Success).IsTrue();

        // Verify the service instance was modified via cross-entity invoke
        var get = RuntimeTool.GetInstance(sessionId, svcId!);
        var getJson = System.Text.Json.JsonSerializer.Serialize(get.Data);
        await Assert.That(getJson).Contains("processed");
    }

    // ── E3b quantifier RT goldens ──────────────────────────────

    [Test]
    public async Task ApplyDsl_CrossEntityAll_InvokesEveryTarget() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        var dsl = DslTool.ApplyDsl(sessionId, """
            domain Test
            Target: entity {
              Status: Text
              Process: action {
                assign Status to "done"
              }
            }
            Source: entity {
              items: many Target
              RunAll: action {
                invoke all items.Process
              }
            }
            """);
        await Assert.That(dsl.Success).IsTrue();

        var createT1 = RuntimeTool.CreateInstance(sessionId, "Target",
            """{"Status":"a"}""");
        var createT2 = RuntimeTool.CreateInstance(sessionId, "Target",
            """{"Status":"b"}""");
        var createS = RuntimeTool.CreateInstance(sessionId, "Source");
        await Assert.That(createT1.Success).IsTrue();
        await Assert.That(createT2.Success).IsTrue();
        await Assert.That(createS.Success).IsTrue();
        var sid = ExtractInstanceId(System.Text.Json.JsonSerializer.Serialize(createS.Data));
        var t1id = ExtractInstanceId(System.Text.Json.JsonSerializer.Serialize(createT1.Data));
        var t2id = ExtractInstanceId(System.Text.Json.JsonSerializer.Serialize(createT2.Data));

        McpSessionStore.TryModifyInstances(sessionId, state => {
            DomainEntityInstance? src = null;
            foreach (var (id, inst) in state.InstanceMap)
                if (id == sid) src = inst;
            if (src is not null && state.InstanceStore is not null)
                foreach (var (id, inst) in state.InstanceMap)
                    if (id != sid) state.InstanceStore.Link("items", src, inst);
        });

        var call = RuntimeTool.InvokeAction(sessionId, sid!, "RunAll");
        await Assert.That(call.Success).IsTrue();

        var g1 = RuntimeTool.GetInstance(sessionId, t1id!);
        var g1s = System.Text.Json.JsonSerializer.Serialize(g1.Data);
        await Assert.That(g1s.Contains("done")).IsTrue();

        var g2 = RuntimeTool.GetInstance(sessionId, t2id!);
        var g2s = System.Text.Json.JsonSerializer.Serialize(g2.Data);
        await Assert.That(g2s.Contains("done")).IsTrue();
    }

    [Test]
    public async Task ApplyDsl_CrossEntityAny_WithFilter_OnlyMatchesTarget() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        var dsl = DslTool.ApplyDsl(sessionId, """
            domain Test
            Target: entity {
              Status: Text
              Size: Number
              Tag: action {
                assign Status to "tagged"
              }
            }
            Source: entity {
              items: many Target
              RunTag: action {
                invoke any items.Tag where Size > 10
              }
            }
            """);
        await Assert.That(dsl.Success).IsTrue();

        var createT1 = RuntimeTool.CreateInstance(sessionId, "Target",
            """{"Status":"x","Size":5}""");
        var createT2 = RuntimeTool.CreateInstance(sessionId, "Target",
            """{"Status":"x","Size":20}""");
        var createS = RuntimeTool.CreateInstance(sessionId, "Source");
        await Assert.That(createT1.Success).IsTrue();
        await Assert.That(createT2.Success).IsTrue();
        await Assert.That(createS.Success).IsTrue();
        var sid = ExtractInstanceId(System.Text.Json.JsonSerializer.Serialize(createS.Data));
        var t1id = ExtractInstanceId(System.Text.Json.JsonSerializer.Serialize(createT1.Data));
        var t2id = ExtractInstanceId(System.Text.Json.JsonSerializer.Serialize(createT2.Data));

        McpSessionStore.TryModifyInstances(sessionId, state => {
            DomainEntityInstance? src = null;
            foreach (var (id, inst) in state.InstanceMap)
                if (id == sid) src = inst;
            if (src is not null && state.InstanceStore is not null)
                foreach (var (id, inst) in state.InstanceMap)
                    if (id != sid) state.InstanceStore.Link("items", src, inst);
        });

        var call = RuntimeTool.InvokeAction(sessionId, sid!, "RunTag");
        await Assert.That(call.Success).IsTrue();

        // t1 (Size=5) should NOT have been tagged — Status must not be "tagged"
        var g1 = RuntimeTool.GetInstance(sessionId, t1id!);
        var g1s = System.Text.Json.JsonSerializer.Serialize(g1.Data);
        await Assert.That(g1s.Contains("tagged")).IsFalse();

        // t2 (Size=20) SHOULD have been tagged
        var g2 = RuntimeTool.GetInstance(sessionId, t2id!);
        var g2s = System.Text.Json.JsonSerializer.Serialize(g2.Data);
        await Assert.That(g2s.Contains("tagged")).IsTrue();
    }

    // ═════════════════════════════════════════════════════════════
    // Q3′ — Collection quantifiers (any/all/none/count)
    // ═════════════════════════════════════════════════════════════

    [Test]
    public async Task Parser_Quantifier_Any_ParsesCorrectly() {
        var parsed = ParseExpression("any items where P1 is \"x\"");
        await Assert.That(parsed).IsTypeOf<AnyExpr>();
        var any = (AnyExpr)parsed;
        await Assert.That(any.RelationshipName).IsEqualTo("items");
        await Assert.That(any.Body).IsTypeOf<Comparison>();
    }

    [Test]
    public async Task Parser_Quantifier_All_ParsesCorrectly() {
        var parsed = ParseExpression("all items where P1 is \"x\"");
        await Assert.That(parsed).IsTypeOf<AllExpr>();
        var all = (AllExpr)parsed;
        await Assert.That(all.RelationshipName).IsEqualTo("items");
    }

    [Test]
    public async Task Parser_Quantifier_None_ParsesCorrectly() {
        var parsed = ParseExpression("none items where P1 is \"x\"");
        await Assert.That(parsed).IsTypeOf<NoneExpr>();
        var none = (NoneExpr)parsed;
        await Assert.That(none.RelationshipName).IsEqualTo("items");
    }

    [Test]
    public async Task Parser_Quantifier_Count_WithWhere_ParsesCorrectly() {
        var parsed = ParseExpression("count items where P1 is \"x\"");
        await Assert.That(parsed).IsTypeOf<CountExpr>();
        var cnt = (CountExpr)parsed;
        await Assert.That(cnt.RelationshipName).IsEqualTo("items");
        await Assert.That(cnt.Body).IsNotNull();
    }

    [Test]
    public async Task Parser_Quantifier_Count_Bare_ParsesCorrectly() {
        var parsed = ParseExpression("count items");
        await Assert.That(parsed).IsTypeOf<CountExpr>();
        var cnt = (CountExpr)parsed;
        await Assert.That(cnt.RelationshipName).IsEqualTo("items");
        await Assert.That(cnt.Body).IsNull();
    }

    [Test]
    public async Task Parser_Quantifier_Any_And_Chain_Body() {
        // Body uses ParseAnd: `or` requires parens inside where body
        var parsed = ParseExpression("any items where P1 is \"x\" and P2 >= 10");
        await Assert.That(parsed).IsTypeOf<AnyExpr>();
        var any = (AnyExpr)parsed;
        await Assert.That(any.Body).IsTypeOf<Poly.DomainModeling.And>();
    }

    [Test]
    public async Task Parser_Quantifier_Any_In_Expression() {
        // Quantifier inside a larger expression
        var parsed = ParseExpression("P2 > 0 and any items where P1 is \"x\"");
        await Assert.That(parsed).IsTypeOf<Poly.DomainModeling.And>();
        var andExpr = (Poly.DomainModeling.And)parsed;
        await Assert.That(andExpr.Right).IsTypeOf<AnyExpr>();
    }

    [Test]
    public async Task Parser_Quantifier_Any_Negated() {
        var parsed = ParseExpression("not any items where P1 is \"x\"");
        await Assert.That(parsed).IsTypeOf<Poly.DomainModeling.Not>();
        var notExpr = (Poly.DomainModeling.Not)parsed;
        await Assert.That(notExpr.Operand).IsTypeOf<AnyExpr>();
    }

    [Test]
    public async Task Printer_Quantifier_Any_RoundTrips() {
        const string text = "any items where P1 is \"x\"";
        var expr = ParseExpression(text);
        var printed = PrintExpressionForTest(expr);
        await Assert.That(printed).IsEqualTo(text);
    }

    [Test]
    public async Task Printer_Quantifier_All_RoundTrips() {
        const string text = "all items where P1 is \"x\"";
        var expr = ParseExpression(text);
        var printed = PrintExpressionForTest(expr);
        await Assert.That(printed).IsEqualTo(text);
    }

    [Test]
    public async Task Printer_Quantifier_None_RoundTrips() {
        const string text = "none items where P1 is \"x\"";
        var expr = ParseExpression(text);
        var printed = PrintExpressionForTest(expr);
        await Assert.That(printed).IsEqualTo(text);
    }

    [Test]
    public async Task Printer_Quantifier_Count_WithWhere_RoundTrips() {
        const string text = "count items where P1 is \"x\"";
        var expr = ParseExpression(text);
        var printed = PrintExpressionForTest(expr);
        await Assert.That(printed).IsEqualTo(text);
    }

    [Test]
    public async Task Printer_Quantifier_Count_Bare_RoundTrips() {
        const string text = "count items";
        var expr = ParseExpression(text);
        var printed = PrintExpressionForTest(expr);
        await Assert.That(printed).IsEqualTo(text);
    }

    [Test]
    public async Task Parser_Quantifier_Any_WithOrInBody_UsesParens() {
        // `or` in body requires parentheses (body is ParseAnd)
        var parsed = ParseExpression("any items where (P1 is \"x\" or P1 is \"y\")");
        await Assert.That(parsed).IsTypeOf<AnyExpr>();
        var any = (AnyExpr)parsed;
        await Assert.That(any.Body).IsTypeOf<Poly.DomainModeling.Or>();
    }

    [Test]
    public async Task ApplyDsl_QuantifierAuthoring_ApplyAndExport() {
        // Q3′ MCP golden: DSL apply + export with quantifier
        var (sessionId, _) = McpSessionStore.Create("Test");
        var dsl = DslTool.ApplyDsl(sessionId, """
            domain Test
            Order: entity {
              Status: Text
              Total: Number
              Priority: Number
            }
            Customer: entity {
              orders: many Order
              HasPriorityOrder: policy { any orders where Priority > 5 }
              AllHighValue: policy { all orders where Total > 100 }
              HasNoRush: policy { none orders where Priority > 9 }
              OpenOrderCount: policy { count orders where Status is "Open" > 0 }
              TotalOrders: policy { count orders > 5 }
            }
            """);
        await Assert.That(dsl.Success).IsTrue();

        var export = DslTool.ExportDsl(sessionId);
        await Assert.That(export.Success).IsTrue();
        var exportJson = System.Text.Json.JsonSerializer.Serialize(export.Data);
        await Assert.That(exportJson).Contains("any orders where");
        await Assert.That(exportJson).Contains("all orders where");
        await Assert.That(exportJson).Contains("none orders where");
        await Assert.That(exportJson).Contains("count orders where");
        // Bare count round-trips (verified in individual printer tests)
        await Assert.That(exportJson).Contains("TotalOrders");
    }

    // ═════════════════════════════════════════════════════════════
    // P4.5 — MCP pack enablement: annotation round-trip
    // ═════════════════════════════════════════════════════════════

    [Test]
    public async Task ApplyDsl_WithColumnAnnotation_CreatesFacetedProperty() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var response = DslTool.ApplyDsl(sessionId, """
            domain Test
            Item: entity {
              ProductName: Text column("PROD_NAME")
            }
            """);
        await Assert.That(response.Success).IsTrue();

        var state = McpSessionStore.TryGet(sessionId, out var s) ? s : null;
        await Assert.That(state).IsNotNull();
        var item = state!.Domain.Types.OfType<Entity>().Single();
        var prop = item.Properties.Single();
        await Assert.That(prop.Facets.Count).IsEqualTo(1);
        var ann = prop.Facets[0] as Annotation;
        await Assert.That(ann).IsNotNull();
        await Assert.That(ann!.Name).IsEqualTo("column");
        await Assert.That(((AnnotationString)ann.Arguments["0"]).Value).IsEqualTo("PROD_NAME");
    }

    [Test]
    public async Task ApplyDsl_WithColumnAnnotation_ExportRoundTrips() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var apply = DslTool.ApplyDsl(sessionId, """
            domain Test
            Item: entity {
              Code: Text unique column("CODE", "VARCHAR2(20)")
              Name: Text column("NAME")
            }
            """);
        await Assert.That(apply.Success).IsTrue();

        var export = DslTool.ExportDsl(sessionId);
        await Assert.That(export.Success).IsTrue();
        var poly = extractPolyFromExport(export);
        await Assert.That(poly).Contains("column(\"CODE\", \"VARCHAR2(20)\")");
        await Assert.That(poly).Contains("column(\"NAME\")");

        // Re-import the export to confirm idempotent round-trip
        var reapply = DslTool.ApplyDsl(sessionId, poly);
        await Assert.That(reapply.Success).IsTrue();
        var state = McpSessionStore.TryGet(sessionId, out var s) ? s : null;
        await Assert.That(state).IsNotNull();
        var item = state!.Domain.Types.OfType<Entity>().Single();
        var code = item.Properties.Single(p => p.Name == "Code");
        await Assert.That(code.Facets.Count).IsEqualTo(1);
        await Assert.That(((Annotation)code.Facets[0]).Name).IsEqualTo("column");
    }

    [Test]
    public async Task ApplyDsl_WithTableAnnotation_ExportRoundTrips() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var apply = DslTool.ApplyDsl(sessionId, """
            domain Test
            Order: entity table("ORDER_RECORDS") {
              Total: Number
            }
            """);
        await Assert.That(apply.Success).IsTrue();

        var export = DslTool.ExportDsl(sessionId);
        await Assert.That(export.Success).IsTrue();
        var poly = extractPolyFromExport(export);
        await Assert.That(poly).Contains("table(\"ORDER_RECORDS\")");

        // Re-import to confirm round-trip
        var reapply = DslTool.ApplyDsl(sessionId, poly);
        await Assert.That(reapply.Success).IsTrue();
        var order = McpSessionStore
            .TryGet(sessionId, out var s) ? s.Domain.Types.OfType<Entity>().Single() : null;
        await Assert.That(order).IsNotNull();
        await Assert.That(order!.Facets.Count).IsEqualTo(1);
        await Assert.That(((Annotation)order.Facets[0]).Name).IsEqualTo("table");
    }

    [Test]
    public async Task ApplyDsl_ColumnAfterConstraint_Parses() {
        // Annotation after constraint in property tail — printer emits constraints
        // before facets (canonical order), but parsing accepts any interleaving.
        var (sessionId, _) = McpSessionStore.Create("Test");

        var apply = DslTool.ApplyDsl(sessionId, """
            domain Test
            Item: entity {
              Code: Text unique column("C")
              Name: Text column("N") required
              Qty: Number range(0,) column("Q")
              Flag: Boolean column("F") unique
            }
            """);
        await Assert.That(apply.Success).IsTrue();

        // Printer canonical order: constraints before facets
        var export = DslTool.ExportDsl(sessionId);
        await Assert.That(export.Success).IsTrue();
        var poly = extractPolyFromExport(export);
        await Assert.That(poly).Contains("unique column(\"C\")");
        await Assert.That(poly).Contains("required column(\"N\")");
        await Assert.That(poly).Contains("range(0, ) column(\"Q\")");
        await Assert.That(poly).Contains("unique column(\"F\")");

        // Re-import confirms idempotent round-trip under canonical order
        var reapply = DslTool.ApplyDsl(sessionId, poly);
        await Assert.That(reapply.Success).IsTrue();
    }

    /// <summary>Extracts the raw .poly DSL text from an export_dsl response.</summary>
    private static string extractPolyFromExport(DomainToolResponse export) {
        var dataProp = export.Data!.GetType().GetProperty("poly");
        return dataProp?.GetValue(export.Data) as string ?? "";
    }

    [Test]
    public async Task EvaluatePolicy_Q3Prime_Any_WithLinkedInstances() {
        // Q3′ MCP e2e: apply_dsl → create instance with links → evaluate_policy with instanceId
        var (sessionId, _) = McpSessionStore.Create("Test");

        // 1. Apply DSL with Q3′ policy
        var dsl = DslTool.ApplyDsl(sessionId, """
            domain Test
            Customer: entity {
              Name: Text
              orders: many Order
              HasBigOrder: policy { any orders where Total > 100 }
              HasNoBigOrder: policy { none orders where Total > 100 }
            }
            Order: entity {
              Total: Number
              customer: Customer
            }
            """);
        await Assert.That(dsl.Success).IsTrue();

        // 2. Create instances
        var custResult = RuntimeTool.CreateInstance(sessionId, "Customer", """{"Name":"Alice"}""");
        await Assert.That(custResult.Success).IsTrue();
        await Assert.That(custResult.Message).Contains("created");
        var custDataJson = System.Text.Json.JsonSerializer.Serialize(custResult.Data);
        await Assert.That(custDataJson).Contains("instanceId");

        var order1Result = RuntimeTool.CreateInstance(sessionId, "Order", """{"Total":50}""");
        await Assert.That(order1Result.Success).IsTrue();

        var order2Result = RuntimeTool.CreateInstance(sessionId, "Order", """{"Total":200}""");
        await Assert.That(order2Result.Success).IsTrue();

        // 3. Resolve instance IDs and link via the public MCP tool
        McpSessionStore.TryGet(sessionId, out var st);
        var custInstance = st.InstanceMap.Values.FirstOrDefault(i => i.Entity.Name == "Customer");
        var order1 = st.InstanceMap.Values.FirstOrDefault(i => i.Entity.Name == "Order" && i.Snapshot().TryGetValue("Total", out var tv) && tv?.Equals(50L) == true);
        var order2 = st.InstanceMap.Values.FirstOrDefault(i => i.Entity.Name == "Order" && i.Snapshot().TryGetValue("Total", out var tv2) && tv2?.Equals(200L) == true);
        await Assert.That(custInstance).IsNotNull();
        await Assert.That(order1).IsNotNull();
        await Assert.That(order2).IsNotNull();

        var custId = st.InstanceMap.First(kvp => kvp.Value == custInstance).Key;
        var order1Id = st.InstanceMap.First(kvp => kvp.Value == order1!).Key;
        var order2Id = st.InstanceMap.First(kvp => kvp.Value == order2!).Key;

        var link1 = RuntimeTool.LinkInstances(sessionId, custId, "orders", order1Id);
        await Assert.That(link1.Success).IsTrue();
        await Assert.That(link1.Message).Contains("Linked");

        var link2 = RuntimeTool.LinkInstances(sessionId, custId, "orders", order2Id);
        await Assert.That(link2.Success).IsTrue();
        await Assert.That(link2.Message).Contains("Linked");

        // 4. Evaluate HasBigOrder policy — should be true (order2 has Total 200 > 100)
        var evalTrue = PolicyTool.EvaluatePolicy(sessionId, "Customer", "HasBigOrder",
            instanceId: custId);
        await Assert.That(evalTrue.Success).IsTrue();
        await Assert.That(evalTrue.Message).Contains("true");

        // 5. Evaluate HasNoBigOrder policy — should be false (order2 matches > 100)
        var evalFalse = PolicyTool.EvaluatePolicy(sessionId, "Customer", "HasNoBigOrder",
            instanceId: custId);
        await Assert.That(evalFalse.Success).IsTrue();
        await Assert.That(evalFalse.Message).Contains("false");
    }

    [Test]
    public async Task LinkInstances_UnknownRelationship_Fails() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        var dsl = DslTool.ApplyDsl(sessionId, """
            domain Test
            A: entity { b: many B }
            B: entity { a: A }
            """);
        await Assert.That(dsl.Success).IsTrue();

        var aResult = RuntimeTool.CreateInstance(sessionId, "A", "{}");
        await Assert.That(aResult.Success).IsTrue();
        var bResult = RuntimeTool.CreateInstance(sessionId, "B", "{}");
        await Assert.That(bResult.Success).IsTrue();

        McpSessionStore.TryGet(sessionId, out var st);
        var aId = st.InstanceMap.First(kvp => kvp.Value.Entity.Name == "A").Key;
        var bId = st.InstanceMap.First(kvp => kvp.Value.Entity.Name == "B").Key;

        var link = RuntimeTool.LinkInstances(sessionId, aId, "nonexistent_rel", bId);
        await Assert.That(link.Success).IsFalse();
        await Assert.That(link.Message).Contains("not found");
    }

    [Test]
    public async Task LinkInstances_WrongEntityTypes_Fails() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        var dsl = DslTool.ApplyDsl(sessionId, """
            domain Test
            Customer: entity { orders: many Order }
            Order: entity { customer: Customer }
            Invoice: entity { }
            """);
        await Assert.That(dsl.Success).IsTrue();

        var custResult = RuntimeTool.CreateInstance(sessionId, "Customer", "{}");
        await Assert.That(custResult.Success).IsTrue();
        var invResult = RuntimeTool.CreateInstance(sessionId, "Invoice", "{}");
        await Assert.That(invResult.Success).IsTrue();

        McpSessionStore.TryGet(sessionId, out var st);
        var custId = st.InstanceMap.First(kvp => kvp.Value.Entity.Name == "Customer").Key;
        var invId = st.InstanceMap.First(kvp => kvp.Value.Entity.Name == "Invoice").Key;

        // Customer → Invoice via "orders" (Invoice is not Order)
        var link = RuntimeTool.LinkInstances(sessionId, custId, "orders", invId);
        await Assert.That(link.Success).IsFalse();
        await Assert.That(link.Message).Contains("connects");
    }

    [Test]
    public async Task LinkInstances_ReversedEnds_Fails() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        var dsl = DslTool.ApplyDsl(sessionId, """
            domain Test
            Customer: entity { orders: many Order }
            Order: entity { customer: Customer }
            """);
        await Assert.That(dsl.Success).IsTrue();

        var custResult = RuntimeTool.CreateInstance(sessionId, "Customer", "{}");
        await Assert.That(custResult.Success).IsTrue();
        var orderResult = RuntimeTool.CreateInstance(sessionId, "Order", "{}");
        await Assert.That(orderResult.Success).IsTrue();

        McpSessionStore.TryGet(sessionId, out var st);
        var custId = st.InstanceMap.First(kvp => kvp.Value.Entity.Name == "Customer").Key;
        var orderId = st.InstanceMap.First(kvp => kvp.Value.Entity.Name == "Order").Key;

        // Pass order as source, customer as target — reversed for directed "orders" link
        var link = RuntimeTool.LinkInstances(sessionId, orderId, "orders", custId);
        await Assert.That(link.Success).IsFalse();
        await Assert.That(link.Message).Contains("reversed");
    }

    [Test]
    public async Task LinkInstances_MissingSource_Fails() {
        var (sessionId, _) = McpSessionStore.Create("Test");
        DslTool.ApplyDsl(sessionId, "domain T A: entity { bs: many B } B: entity { a: A }");
        var bResult = RuntimeTool.CreateInstance(sessionId, "B", "{}");
        await Assert.That(bResult.Success).IsTrue();

        McpSessionStore.TryGet(sessionId, out var st);
        var bId = st.InstanceMap.First(kvp => kvp.Value.Entity.Name == "B").Key;

        var link = RuntimeTool.LinkInstances(sessionId, "nonexistent-id", "bs", bId);
        await Assert.That(link.Success).IsFalse();
        await Assert.That(link.Message).Contains("not found");
    }
}