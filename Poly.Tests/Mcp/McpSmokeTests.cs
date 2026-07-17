using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
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

            relationship Places from Customer to Order many
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
    public async Task ApplyDsl_WithRequire_BlocksCallActionWhenPolicyFails() {
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
        var result = instance.CallAction("Submit");
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailedGuards.Count).IsGreaterThan(0);
        await Assert.That(result.FailedGuards).Contains("HighScore");
        await Assert.That(instance.CurrentStage).IsEqualTo("Draft");

        // Instance with Score=15 (passes: Score > 10) → succeeds
        var instance2 = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Score"] = 15L });
        var result2 = instance2.CallAction("Submit");
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
}