using Poly.Mcp.Sessions;
using Poly.Mcp.Tools;

namespace Poly.Tests.Mcp;

/// <summary>
/// Tests for OracleTool: describe_domain_element and simulate_policy.
/// </summary>
public class OracleToolTests {
    [Test]
    public async Task DescribeDomainElement_Entity_AfterAdd() {
        var response = SessionTool.CreateDomainSession("OracleTest");
        await Assert.That(response.Success).IsTrue();
        var sessionId = response.SessionId!;

        // Add an entity with a property and stage
        var r1 = EvolveTool.Add(sessionId, "entity", """{"name":"Widget"}""");
        await Assert.That(r1.Success).IsTrue();
        var r2 = EvolveTool.Add(sessionId, "property", """{"entityName":"Widget","name":"Name","typeName":"Text"}""");
        await Assert.That(r2.Success).IsTrue();
        var r3 = EvolveTool.Add(sessionId, "stage", """{"entityName":"Widget","name":"Active"}""");
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

    // ── V0′.2: Policy describe smoke test ──────────────────────

    [Test]
    public async Task DescribeDomainElement_Policy_IncludesExpressionEnglish() {
        var (sessionId, _) = McpSessionStore.Create("OracleTest");

        // Add entity with a property and policy
        var r1 = EvolveTool.Add(sessionId, "entity", """{"name":"Person"}""");
        await Assert.That(r1.Success).IsTrue();
        var r1b = EvolveTool.Add(sessionId, "property", """{"entityName":"Person","name":"Age","typeName":"Number"}""");
        await Assert.That(r1b.Success).IsTrue();
        var r2 = EvolveTool.Add(sessionId, "policy", """{"entityName":"Person","name":"Adult","expression":"Age >= 18"}""");
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
        EvolveTool.Add(sessionId, "entity", """{"name":"Order"}""");
        EvolveTool.Add(sessionId, "stage", """{"entityName":"Order","name":"Active"}""");
        EvolveTool.Add(sessionId, "entity", """{"name":"Invoice"}""");
        EvolveTool.Add(sessionId, "stage", """{"entityName":"Invoice","name":"Active"}""");

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
            "Age >= 18",
            @"{""Age"":25}");

        await Assert.That(response.Success).IsTrue();
        var dataJson = System.Text.Json.JsonSerializer.Serialize(response.Data);
        await Assert.That(dataJson).Contains("true");
    }

    [Test]
    public async Task SimulatePolicy_AgeGte_FailsForMinor() {
        var response = OracleTool.SimulatePolicy(
            "Age >= 18",
            @"{""Age"":10}");

        await Assert.That(response.Success).IsTrue();
        var dataJson = System.Text.Json.JsonSerializer.Serialize(response.Data);
        await Assert.That(dataJson).Contains("false");
    }

    [Test]
    public async Task SimulatePolicy_And_Works() {
        var response = OracleTool.SimulatePolicy(
            "(Age >= 18) and (Active == true)",
            @"{""Age"":25,""Active"":true}");

        await Assert.That(response.Success).IsTrue();
        var dataJson = System.Text.Json.JsonSerializer.Serialize(response.Data);
        await Assert.That(dataJson).Contains("true");
    }

    [Test]
    public async Task SimulatePolicy_InvalidExpression_Fails() {
        var response = OracleTool.SimulatePolicy("Age >=", @"{""Age"":25}");

        await Assert.That(response.Success).IsFalse();
    }

    [Test]
    public async Task SimulatePolicy_EmptyProperties_Fails() {
        var response = OracleTool.SimulatePolicy(
            "Age >= 18",
            @"{}");

        await Assert.That(response.Success).IsFalse();
    }

    // ── G1: simulate_policy fail-closed on missing properties ──

    [Test]
    public async Task SimulatePolicy_UnknownProperty_FailsClosed() {
        // Expression references "NonExistent" which is not in the subject bag.
        var response = OracleTool.SimulatePolicy(
            "NonExistent == 1",
            @"{""Something"":5}");

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("NonExistent");
        await Assert.That(response.Message).Contains("not present");
    }

    // ── owned-2: relationship navigation in DSL expressions ──

    [Test]
    public async Task SimulatePolicy_RelationshipDsl_WithoutStore_FailsClosed() {
        // Relationship-nav DSL fragment parses, but evaluate without a store
        // is fail-closed (no vacuous bag pass-through). Use create+link+evaluate_policy.
        var response = OracleTool.SimulatePolicy(
            "profile City is \"Metropolis\"",
            @"{""City"":""Metropolis""}");

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("Simulation failed");
    }

    [Test]
    public async Task AddPolicy_RelationshipDsl_ValidSyntax() {
        // Verify the DSL nav fragment is accepted via add(kind: policy)
        var (sessionId, _) = McpSessionStore.Create("Owned2Test");
        DslTool.ApplyDsl(sessionId, """
            domain Owned2Test
            Profile: entity { City: Text }
            Customer: entity {
              Name: Text
              profile: owned Profile
            }
            """);

        // Add policy using the DSL relationship-nav fragment
        var response = EvolveTool.Add(sessionId, "policy",
            """{"entityName":"Customer","name":"IsUrban","expression":"profile City is \"Metropolis\""}""");

        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Message).Contains("IsUrban");
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

        var r1 = EvolveTool.Add(sessionId, "entity", """{"name":"Task"}""");
        await Assert.That(r1.Success).IsTrue();
        EvolveTool.Add(sessionId, "property", """{"entityName":"Task","name":"Title","typeName":"Text"}""");
        EvolveTool.Add(sessionId, "property", """{"entityName":"Task","name":"IsComplete","typeName":"Boolean"}""");

        var response = QueryTool.GetDomainSuggestions(sessionId);
        await Assert.That(response.Success).IsTrue();
        var dataJson = System.Text.Json.JsonSerializer.Serialize(response.Data);
        await Assert.That(dataJson.ToLowerInvariant()).Contains("stage");
        // B4: the policy hint must teach the unified surface, not the deleted add_policy.
        await Assert.That(dataJson).Contains("add(kind: policy)");
        await Assert.That(dataJson).DoesNotContain("add_policy");
    }

    [Test]
    public async Task GetDomainSuggestions_UnknownSession_Fails() {
        var response = QueryTool.GetDomainSuggestions("nonexistent");
        await Assert.That(response.Success).IsFalse();
    }
}