using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Effects;
using Poly.DomainModeling.Evolution;
using Poly.Mcp.Sessions;
using Poly.Mcp.Tools;

namespace Poly.Tests.Mcp;

/// <summary>
/// Tests for OracleTool: lower_expression, describe_expression, and describe_domain_element.
/// </summary>
public class OracleToolTests {
    [Test]
    public async Task LowerExpression_AgeGte_Succeeds() {
        var response = OracleTool.LowerExpression(@"{""property"":""Age"",""op"":"">="",""value"":18}");

        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Message).Contains("lowered");
        await Assert.That(response.Data).IsNotNull();

        // Verify the AST mentions "GreaterThanOrEqual" or "Age" or "18"
        var dataJson = System.Text.Json.JsonSerializer.Serialize(response.Data);
        await Assert.That(dataJson).Contains("GreaterThanOrEqual");
        await Assert.That(dataJson).Contains("Age");
        await Assert.That(dataJson).Contains("18");
    }

    [Test]
    public async Task LowerExpression_BadJson_Fails() {
        var response = OracleTool.LowerExpression("not json");

        await Assert.That(response.Success).IsFalse();
    }

    [Test]
    public async Task LowerExpression_Empty_Fails() {
        var response = OracleTool.LowerExpression("");

        await Assert.That(response.Success).IsFalse();
    }

    [Test]
    public async Task DescribeExpression_AgeGte_PlainEnglish() {
        var response = OracleTool.DescribeExpression(@"{""property"":""Age"",""op"":"">="",""value"":18}");

        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Data).IsNotNull();

        var dataJson = System.Text.Json.JsonSerializer.Serialize(response.Data);
        await Assert.That(dataJson).Contains("Age");
        await Assert.That(dataJson).Contains("18");
        // Plain English should mention "at least" or "greater than"
        await Assert.That(dataJson.ToLowerInvariant()).Contains("at least".ToLowerInvariant());
    }

    [Test]
    public async Task DescribeExpression_Composite_Works() {
        var response = OracleTool.DescribeExpression(
            @"{""and"":[{""property"":""Age"",""op"":"">="",""value"":18},{""property"":""Active"",""op"":""=="",""value"":true}]}");

        await Assert.That(response.Success).IsTrue();
        var dataJson = System.Text.Json.JsonSerializer.Serialize(response.Data);
        await Assert.That(dataJson).Contains("and");
    }

    [Test]
    public async Task DescribeDomainElement_Entity_AfterAdd() {
        var response = SessionTool.CreateDomainSession("OracleTest");
        await Assert.That(response.Success).IsTrue();
        var sessionId = response.SessionId!;

        // Add an entity with a property and stage
        var r1 = EvolveTool.AddEntity(sessionId, "Widget");
        await Assert.That(r1.Success).IsTrue();
        var r2 = EvolveTool.AddProperty(sessionId, "Widget", "Name", "Text");
        await Assert.That(r2.Success).IsTrue();
        var r3 = EvolveTool.AddStage(sessionId, "Widget", "Active");
        await Assert.That(r3.Success).IsTrue();

        // Now describe the entity
        var desc = OracleTool.DescribeDomainElement(sessionId, "entity", "Widget");
        await Assert.That(desc.Success).IsTrue();
        await Assert.That(desc.Data).IsNotNull();

        var descJson = System.Text.Json.JsonSerializer.Serialize(desc.Data);
        await Assert.That(descJson).Contains("Widget");
        await Assert.That(descJson).Contains("1 stages");
    }

    [Test]
    public async Task DescribeDomainElement_Unknown_Fails() {
        var (sessionId, _) = McpSessionStore.Create("OracleTest");

        var desc = OracleTool.DescribeDomainElement(sessionId, "entity", "NonExistent");
        await Assert.That(desc.Success).IsFalse();
    }

    [Test]
    public async Task DescribeDomainElement_UnknownKind_Fails() {
        var (sessionId, _) = McpSessionStore.Create("OracleTest");

        var desc = OracleTool.DescribeDomainElement(sessionId, "garbage", "something");
        await Assert.That(desc.Success).IsFalse();
    }

    [Test]
    public async Task Chain_LowerThenDescribe_SameJson() {
        // Chain smoke: lower → describe same JSON both succeed
        var json = @"{""property"":""Status"",""op"":""=="",""value"":""Active""}";

        var lower = OracleTool.LowerExpression(json);
        await Assert.That(lower.Success).IsTrue();

        var describe = OracleTool.DescribeExpression(json);
        await Assert.That(describe.Success).IsTrue();

        var describeData = System.Text.Json.JsonSerializer.Serialize(describe.Data);
        await Assert.That(describeData).Contains("Status");
        await Assert.That(describeData).Contains("Active");
    }

    // ── V0′.2: Policy describe smoke test ──────────────────────

    [Test]
    public async Task DescribeDomainElement_Policy_IncludesExpressionEnglish() {
        var (sessionId, _) = McpSessionStore.Create("OracleTest");

        // Add entity with a property and policy
        var r1 = EvolveTool.AddEntity(sessionId, "Person");
        await Assert.That(r1.Success).IsTrue();
        var r1b = EvolveTool.AddProperty(sessionId, "Person", "Age", "Number");
        await Assert.That(r1b.Success).IsTrue();
        var r2 = PolicyTool.AddPolicy(sessionId, "Person", "Adult",
            @"{""property"":""Age"",""op"":"">="",""value"":18}");
        await Assert.That(r2.Success).IsTrue();

        var desc = OracleTool.DescribeDomainElement(sessionId, "policy", "Adult");
        await Assert.That(desc.Success).IsTrue();

        // The plain-English description should mention the expression
        var descJson = System.Text.Json.JsonSerializer.Serialize(desc.Data);
        await Assert.That(descJson.ToLowerInvariant()).Contains("at least");
        await Assert.That(descJson).Contains("Adult");
    }

    [Test]
    public async Task DescribeDomainElement_Stage_WithEntityName_Disambiguates() {
        var (sessionId, _) = McpSessionStore.Create("OracleTest");

        // Create two entities with same-named stage
        EvolveTool.AddEntity(sessionId, "Order");
        EvolveTool.AddStage(sessionId, "Order", "Active");
        EvolveTool.AddEntity(sessionId, "Invoice");
        EvolveTool.AddStage(sessionId, "Invoice", "Active");

        // Without entityName, should find first match
        var desc = OracleTool.DescribeDomainElement(sessionId, "stage", "Active");
        await Assert.That(desc.Success).IsTrue();

        // With entityName, should find the specific one
        var desc2 = OracleTool.DescribeDomainElement(sessionId, "stage", "Active", entityName: "Invoice");
        await Assert.That(desc2.Success).IsTrue();
        var json2 = System.Text.Json.JsonSerializer.Serialize(desc2.Data);
        await Assert.That(json2).Contains("Invoice");
    }

    // ── S0: simulate_policy tests ──────────────────────────────

    [Test]
    public async Task SimulatePolicy_AgeGte_PassesForAdult() {
        var response = OracleTool.SimulatePolicy(
            @"{""property"":""Age"",""op"":"">="",""value"":18}",
            @"{""Age"":25}");

        await Assert.That(response.Success).IsTrue();
        var dataJson = System.Text.Json.JsonSerializer.Serialize(response.Data);
        await Assert.That(dataJson).Contains("true");
    }

    [Test]
    public async Task SimulatePolicy_AgeGte_FailsForMinor() {
        var response = OracleTool.SimulatePolicy(
            @"{""property"":""Age"",""op"":"">="",""value"":18}",
            @"{""Age"":10}");

        await Assert.That(response.Success).IsTrue();
        var dataJson = System.Text.Json.JsonSerializer.Serialize(response.Data);
        await Assert.That(dataJson).Contains("false");
    }

    [Test]
    public async Task SimulatePolicy_And_Works() {
        var response = OracleTool.SimulatePolicy(
            @"{""and"":[{""property"":""Age"",""op"":"">="",""value"":18},{""property"":""Active"",""op"":""=="",""value"":true}]}",
            @"{""Age"":25,""Active"":true}");

        await Assert.That(response.Success).IsTrue();
        var dataJson = System.Text.Json.JsonSerializer.Serialize(response.Data);
        await Assert.That(dataJson).Contains("true");
    }

    [Test]
    public async Task SimulatePolicy_InvalidExpression_Fails() {
        var response = OracleTool.SimulatePolicy("not json", @"{""Age"":25}");

        await Assert.That(response.Success).IsFalse();
    }

    [Test]
    public async Task SimulatePolicy_EmptyProperties_Fails() {
        var response = OracleTool.SimulatePolicy(
            @"{""property"":""Age"",""op"":"">="",""value"":18}",
            @"{}");

        await Assert.That(response.Success).IsFalse();
    }

    // ── G1: simulate_policy fail-closed on missing properties ──

    [Test]
    public async Task SimulatePolicy_UnknownProperty_FailsClosed() {
        // Expression references "NonExistent" which is not in the subject bag.
        var response = OracleTool.SimulatePolicy(
            @"{""property"":""NonExistent"",""op"":""=="",""value"":1}",
            @"{""Something"":5}");

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("NonExistent");
        await Assert.That(response.Message).Contains("not present");
    }

    // ── A2.2: get_domain_suggestions smoke test ────────────────

    [Test]
    public async Task GetDomainSuggestions_EmptyDomain_HasNoSuggestions() {
        var (sessionId, _) = McpSessionStore.Create("SuggestionTest");

        var response = QueryTool.GetDomainSuggestions(sessionId);
        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Message).Contains("No suggestions");
        await Assert.That(response.Data).IsNotNull();
        var dataJson = System.Text.Json.JsonSerializer.Serialize(response.Data);
        await Assert.That(dataJson).Contains("\"count\":0");
    }

    [Test]
    public async Task GetDomainSuggestions_EntityWithPropertiesNoStages_HasSuggestions() {
        var (sessionId, _) = McpSessionStore.Create("SuggestionTest");

        var r1 = EvolveTool.AddEntity(sessionId, "Task");
        await Assert.That(r1.Success).IsTrue();
        EvolveTool.AddProperty(sessionId, "Task", "Title", "Text");
        EvolveTool.AddProperty(sessionId, "Task", "IsComplete", "Boolean");

        var response = QueryTool.GetDomainSuggestions(sessionId);
        await Assert.That(response.Success).IsTrue();
        var dataJson = System.Text.Json.JsonSerializer.Serialize(response.Data);
        await Assert.That(dataJson.ToLowerInvariant()).Contains("stage");
    }

    [Test]
    public async Task GetDomainSuggestions_UnknownSession_Fails() {
        var response = QueryTool.GetDomainSuggestions("nonexistent");
        await Assert.That(response.Success).IsFalse();
    }
}