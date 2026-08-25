using Poly.Mcp.Sessions;
using Poly.Mcp.Tools;

namespace Poly.Tests.Mcp;

/// <summary>mcp-minify-3: unified <c>add</c> tool (kind + payload) for core kinds.</summary>
public class UnifiedAddTests {
    [Test]
    public async Task Add_Entity_Succeeds() {
        var (sessionId, _) = McpSessionStore.Create("UnifiedAddTest");

        var response = EvolveTool.Add(sessionId, "entity", """{"name":"Widget"}""");

        await Assert.That(response.Success).IsTrue();
        var overview = QueryTool.GetDomainOverview(sessionId);
        await Assert.That(overview.Success).IsTrue();
        var data = (DomainOverviewData)overview.Data!;
        await Assert.That(data.EntityNames).Contains("Widget");
    }

    [Test]
    public async Task Add_Property_Succeeds() {
        var (sessionId, _) = McpSessionStore.Create("UnifiedAddTest");
        EvolveTool.Add(sessionId, "entity", """{"name":"Widget"}""");

        var response = EvolveTool.Add(sessionId, "property",
            """{"entityName":"Widget","name":"Weight","typeName":"Number"}""");

        await Assert.That(response.Success).IsTrue();
        var detail = QueryTool.GetEntityDetail(sessionId, "Widget");
        await Assert.That(detail.Success).IsTrue();
        var data = (EntityDetailData)detail.Data!;
        await Assert.That(data.Properties.Select(p => p.Name)).Contains("Weight");
    }

    [Test]
    public async Task Add_Stage_Succeeds() {
        var (sessionId, _) = McpSessionStore.Create("UnifiedAddTest");
        EvolveTool.Add(sessionId, "entity", """{"name":"Order"}""");

        var response = EvolveTool.Add(sessionId, "stage",
            """{"entityName":"Order","name":"Active"}""");

        await Assert.That(response.Success).IsTrue();
        var detail = QueryTool.GetEntityDetail(sessionId, "Order");
        var data = (EntityDetailData)detail.Data!;
        await Assert.That(data.Stages.Select(s => s.Name)).Contains("Active");
    }

    [Test]
    public async Task Add_Action_Succeeds() {
        var (sessionId, _) = McpSessionStore.Create("UnifiedAddTest");
        EvolveTool.Add(sessionId, "entity", """{"name":"Order"}""");

        var response = EvolveTool.Add(sessionId, "action",
            """{"entityName":"Order","name":"Submit"}""");

        await Assert.That(response.Success).IsTrue();
        var detail = QueryTool.GetEntityDetail(sessionId, "Order");
        var data = (EntityDetailData)detail.Data!;
        await Assert.That(data.Actions.Select(a => a.Name)).Contains("Submit");
    }

    [Test]
    public async Task Add_StageAction_Succeeds() {
        var (sessionId, _) = McpSessionStore.Create("UnifiedAddTest");
        EvolveTool.Add(sessionId, "entity", """{"name":"Order"}""");
        EvolveTool.Add(sessionId, "stage", """{"entityName":"Order","name":"Draft"}""");

        var response = EvolveTool.Add(sessionId, "stage_action",
            """{"entityName":"Order","stageName":"Draft","name":"Submit"}""");

        await Assert.That(response.Success).IsTrue();
        var detail = QueryTool.GetEntityDetail(sessionId, "Order");
        var data = (EntityDetailData)detail.Data!;
        var stage = data.Stages.Single(s => s.Name == "Draft");
        await Assert.That(stage.Actions).Contains("Submit");
    }

    [Test]
    public async Task Add_Relationship_Succeeds() {
        var (sessionId, _) = McpSessionStore.Create("UnifiedAddTest");
        EvolveTool.Add(sessionId, "entity", """{"name":"Patron"}""");
        EvolveTool.Add(sessionId, "entity", """{"name":"Loan"}""");

        var response = EvolveTool.Add(sessionId, "relationship",
            """{"name":"Loans","source":"Patron","target":"Loan","cardinality":"OneToMany"}""");

        await Assert.That(response.Success).IsTrue();
        var relationships = QueryTool.GetRelationships(sessionId);
        await Assert.That(relationships.Success).IsTrue();
        var json = System.Text.Json.JsonSerializer.Serialize(relationships.Data);
        await Assert.That(json).Contains("Loans");
    }

    [Test]
    public async Task Add_UnknownKind_Fails() {
        var (sessionId, _) = McpSessionStore.Create("UnifiedAddTest");

        var response = EvolveTool.Add(sessionId, "garbage", """{"name":"X"}""");

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("garbage");
    }

    [Test]
    public async Task Add_Property_MissingEntityName_Fails() {
        var (sessionId, _) = McpSessionStore.Create("UnifiedAddTest");

        var response = EvolveTool.Add(sessionId, "property",
            """{"name":"Weight","typeName":"Number"}""");

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("entityName");
    }

    [Test]
    public async Task Add_InvalidPayloadJson_Fails() {
        var (sessionId, _) = McpSessionStore.Create("UnifiedAddTest");

        var response = EvolveTool.Add(sessionId, "entity", "not json");

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("payload");
    }

    [Test]
    public async Task Add_MissingSession_Fails() {
        var response = EvolveTool.Add("nonexistent", "entity", """{"name":"Widget"}""");

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("not found");
    }

    // ── mcp-minify-4: constraint + policy kinds ──────────────────

    [Test]
    public async Task Add_Constraint_Required_Succeeds() {
        var (sessionId, _) = McpSessionStore.Create("UnifiedAddTest");
        EvolveTool.Add(sessionId, "entity", """{"name":"Order"}""");
        EvolveTool.Add(sessionId, "property",
            """{"entityName":"Order","name":"Code","typeName":"Text"}""");

        var response = EvolveTool.Add(sessionId, "constraint",
            """{"entityName":"Order","propertyName":"Code","type":"Required"}""");

        await Assert.That(response.Success).IsTrue();
        var constraints = EvolveTool.GetConstraints(sessionId, "Order", "Code");
        await Assert.That(constraints.Success).IsTrue();
        var json = System.Text.Json.JsonSerializer.Serialize(constraints.Data);
        await Assert.That(json).Contains("Required");
    }

    [Test]
    public async Task Add_Policy_DslFragment_Succeeds() {
        var (sessionId, _) = McpSessionStore.Create("UnifiedAddTest");
        EvolveTool.Add(sessionId, "entity", """{"name":"Person"}""");
        EvolveTool.Add(sessionId, "property",
            """{"entityName":"Person","name":"Age","typeName":"Number"}""");

        var response = EvolveTool.Add(sessionId, "policy",
            """{"entityName":"Person","name":"Adult","expression":"Age >= 18"}""");

        await Assert.That(response.Success).IsTrue();
        var expr = PolicyTool.GetPolicyExpression(sessionId, "Person", "Adult");
        await Assert.That(expr.Success).IsTrue();
        var json = System.Text.Json.JsonSerializer.Serialize(expr.Data);
        await Assert.That(json).DoesNotContain("property");
    }

    [Test]
    public async Task Add_Policy_InvalidDsl_Fails() {
        var (sessionId, _) = McpSessionStore.Create("UnifiedAddTest");
        EvolveTool.Add(sessionId, "entity", """{"name":"Person"}""");

        var response = EvolveTool.Add(sessionId, "policy",
            """{"entityName":"Person","name":"Broken","expression":"Age >="}""");

        await Assert.That(response.Success).IsFalse();
        var detail = QueryTool.GetEntityDetail(sessionId, "Person");
        var data = (EntityDetailData)detail.Data!;
        await Assert.That(data.Policies).IsEmpty();
    }

    [Test]
    public async Task Add_Policy_JsonBag_Fails() {
        // L5 fail-closed: the payload field named `expression` must be DSL text;
        // a JSON expression bag is rejected by the DSL fragment parser.
        var (sessionId, _) = McpSessionStore.Create("UnifiedAddTest");
        EvolveTool.Add(sessionId, "entity", """{"name":"Person"}""");

        var response = EvolveTool.Add(sessionId, "policy",
            """{"entityName":"Person","name":"Adult","expression":"{\"property\":\"Age\",\"op\":\">=\",\"value\":18}"}""");

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("Invalid policy expression");
    }

    // ── mcp-minify follow-ups B1 / S1 / S5 / S6 ──────────────────

    [Test]
    public async Task Add_Constraint_Pattern_Succeeds() {
        // B1: the documented `pattern` key must work (BuildConstraint reads it).
        var (sessionId, _) = McpSessionStore.Create("UnifiedAddTest");
        EvolveTool.Add(sessionId, "entity", """{"name":"Order"}""");
        EvolveTool.Add(sessionId, "property",
            """{"entityName":"Order","name":"Zip","typeName":"Text"}""");

        var response = EvolveTool.Add(sessionId, "constraint",
            """{"entityName":"Order","propertyName":"Zip","type":"Pattern","pattern":"^[a-z]+$"}""");

        await Assert.That(response.Success).IsTrue();
        var constraints = EvolveTool.GetConstraints(sessionId, "Order", "Zip");
        var json = System.Text.Json.JsonSerializer.Serialize(constraints.Data);
        await Assert.That(json).Contains("Pattern");
    }

    [Test]
    public async Task Add_Constraint_Range_Succeeds() {
        var (sessionId, _) = McpSessionStore.Create("UnifiedAddTest");
        EvolveTool.Add(sessionId, "entity", """{"name":"Order"}""");
        EvolveTool.Add(sessionId, "property",
            """{"entityName":"Order","name":"Total","typeName":"Number"}""");

        var response = EvolveTool.Add(sessionId, "constraint",
            """{"entityName":"Order","propertyName":"Total","type":"Range","min":0,"max":100}""");

        await Assert.That(response.Success).IsTrue();
        var constraints = EvolveTool.GetConstraints(sessionId, "Order", "Total");
        var json = System.Text.Json.JsonSerializer.Serialize(constraints.Data);
        await Assert.That(json).Contains("Range");
    }

    [Test]
    public async Task Add_Constraint_Length_Succeeds() {
        var (sessionId, _) = McpSessionStore.Create("UnifiedAddTest");
        EvolveTool.Add(sessionId, "entity", """{"name":"Order"}""");
        EvolveTool.Add(sessionId, "property",
            """{"entityName":"Order","name":"Code","typeName":"Text"}""");

        var response = EvolveTool.Add(sessionId, "constraint",
            """{"entityName":"Order","propertyName":"Code","type":"Length","min":2,"max":10}""");

        await Assert.That(response.Success).IsTrue();
        var constraints = EvolveTool.GetConstraints(sessionId, "Order", "Code");
        var json = System.Text.Json.JsonSerializer.Serialize(constraints.Data);
        await Assert.That(json).Contains("Length");
    }

    [Test]
    public async Task Add_Relationship_UnknownCardinality_Fails() {
        // S1: invalid cardinality fails closed with the allowed list.
        var (sessionId, _) = McpSessionStore.Create("UnifiedAddTest");
        EvolveTool.Add(sessionId, "entity", """{"name":"Patron"}""");
        EvolveTool.Add(sessionId, "entity", """{"name":"Loan"}""");

        var response = EvolveTool.Add(sessionId, "relationship",
            """{"name":"Loans","source":"Patron","target":"Loan","cardinality":"OneToOnee"}""");

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("OneToMany");
    }

    [Test]
    public async Task Add_Relationship_ManyToOne_Succeeds() {
        // S1: ManyToOne is a documented value and must be stored, not downgraded.
        var (sessionId, _) = McpSessionStore.Create("UnifiedAddTest");
        EvolveTool.Add(sessionId, "entity", """{"name":"Order"}""");
        EvolveTool.Add(sessionId, "entity", """{"name":"Customer"}""");

        var response = EvolveTool.Add(sessionId, "relationship",
            """{"name":"OrderCustomer","source":"Order","target":"Customer","cardinality":"ManyToOne"}""");

        await Assert.That(response.Success).IsTrue();
        var rels = QueryTool.GetRelationships(sessionId);
        var json = System.Text.Json.JsonSerializer.Serialize(rels.Data);
        await Assert.That(json).Contains("ManyToOne");
    }

    [Test]
    public async Task Add_Property_NonStringField_Fails() {
        // S5: a non-string field value is treated as missing (fail closed).
        var (sessionId, _) = McpSessionStore.Create("UnifiedAddTest");
        EvolveTool.Add(sessionId, "entity", """{"name":"Order"}""");

        var response = EvolveTool.Add(sessionId, "property",
            """{"entityName":"Order","name":42,"typeName":"Number"}""");

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("name");
    }

    [Test]
    public async Task Add_MissingSession_InvalidJson_ReportsSessionFirst() {
        // S6: session check precedes payload parse.
        var response = EvolveTool.Add("nonexistent", "entity", "not json");

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("not found");
        await Assert.That(response.Message).DoesNotContain("payload");
    }
}