using Poly.DomainModeling;
using Poly.DomainModeling.Evolution;
using Poly.Mcp.Sessions;
using Poly.Mcp.Tools;

namespace Poly.Tests.Mcp;

/// <summary>mcp-minify-5: unified <c>remove</c> tool (kind + payload, identity fields).</summary>
public class UnifiedRemoveTests {
    [Test]
    public async Task Remove_Entity_Succeeds() {
        var (sessionId, _) = McpSessionStore.Create("UnifiedRemoveTest");
        EvolveTool.Add(sessionId, "entity", """{"name":"Widget"}""");

        var response = EvolveTool.Remove(sessionId, "entity", """{"name":"Widget"}""");

        await Assert.That(response.Success).IsTrue();
        var overview = QueryTool.GetDomainOverview(sessionId);
        var data = (DomainOverviewData)overview.Data!;
        await Assert.That(data.EntityNames).DoesNotContain("Widget");
    }

    [Test]
    public async Task Remove_Property_Succeeds() {
        var (sessionId, _) = McpSessionStore.Create("UnifiedRemoveTest");
        EvolveTool.Add(sessionId, "entity", """{"name":"Widget"}""");
        EvolveTool.Add(sessionId, "property",
            """{"entityName":"Widget","name":"Weight","typeName":"Number"}""");

        var response = EvolveTool.Remove(sessionId, "property",
            """{"entityName":"Widget","name":"Weight"}""");

        await Assert.That(response.Success).IsTrue();
        var detail = QueryTool.GetEntityDetail(sessionId, "Widget");
        var data = (EntityDetailData)detail.Data!;
        await Assert.That(data.Properties.Select(p => p.Name)).DoesNotContain("Weight");
    }

    [Test]
    public async Task Remove_StageAction_Succeeds() {
        var (sessionId, _) = McpSessionStore.Create("UnifiedRemoveTest");
        EvolveTool.Add(sessionId, "entity", """{"name":"Order"}""");
        EvolveTool.Add(sessionId, "stage", """{"entityName":"Order","name":"Draft"}""");
        EvolveTool.Add(sessionId, "stage_action",
            """{"entityName":"Order","stageName":"Draft","name":"Submit"}""");

        var response = EvolveTool.Remove(sessionId, "stage_action",
            """{"entityName":"Order","stageName":"Draft","name":"Submit"}""");

        await Assert.That(response.Success).IsTrue();
        var detail = QueryTool.GetEntityDetail(sessionId, "Order");
        var data = (EntityDetailData)detail.Data!;
        var stage = data.Stages.Single(s => s.Name == "Draft");
        await Assert.That(stage.Actions).DoesNotContain("Submit");
    }

    [Test]
    public async Task Remove_Policy_Succeeds() {
        var (sessionId, _) = McpSessionStore.Create("UnifiedRemoveTest");
        EvolveTool.Add(sessionId, "entity", """{"name":"Person"}""");
        EvolveTool.Add(sessionId, "property",
            """{"entityName":"Person","name":"Age","typeName":"Number"}""");
        EvolveTool.Add(sessionId, "policy",
            """{"entityName":"Person","name":"Adult","expression":"Age >= 18"}""");

        var response = EvolveTool.Remove(sessionId, "policy",
            """{"entityName":"Person","name":"Adult"}""");

        await Assert.That(response.Success).IsTrue();
        var detail = QueryTool.GetEntityDetail(sessionId, "Person");
        var data = (EntityDetailData)detail.Data!;
        await Assert.That(data.Policies).DoesNotContain("Adult");
    }

    [Test]
    public async Task Remove_UnknownKind_Fails() {
        var (sessionId, _) = McpSessionStore.Create("UnifiedRemoveTest");

        var response = EvolveTool.Remove(sessionId, "garbage", """{"name":"X"}""");

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("garbage");
    }

    [Test]
    public async Task Remove_Constraint_NotImplemented_FailsClosed() {
        var (sessionId, _) = McpSessionStore.Create("UnifiedRemoveTest");

        var response = EvolveTool.Remove(sessionId, "constraint",
            """{"entityName":"Order","propertyName":"Code","type":"Required"}""");

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("not implemented");
    }

    [Test]
    public async Task Remove_MissingField_Fails() {
        var (sessionId, _) = McpSessionStore.Create("UnifiedRemoveTest");

        var response = EvolveTool.Remove(sessionId, "property", """{"name":"X"}""");

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("entityName");
    }

    [Test]
    public async Task Remove_MissingSession_Fails() {
        var response = EvolveTool.Remove("nonexistent", "entity", """{"name":"Widget"}""");

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("not found");
    }

    // ── mcp-minify follow-ups B3 / S6 ────────────────────────────

    [Test]
    public async Task Remove_Policy_StageScope_Succeeds() {
        // B3: stage-scoped policy removal via optional stageName payload field.
        var (sessionId, _) = McpSessionStore.Create("UnifiedRemoveTest");
        EvolveTool.Add(sessionId, "entity", """{"name":"Order"}""");
        EvolveTool.Add(sessionId, "stage", """{"entityName":"Order","name":"Active"}""");
        EvolveTool.Add(sessionId, "property", """{"entityName":"Order","name":"Score","typeName":"Number"}""");

        McpSessionStore.Evolve(sessionId, (domain, session) =>
            new DomainEvolution(domain).Evolve()
                .AddPolicyToStage("Order", "Active", "Guard",
                    DomainExpression.GreaterThanOrEqual(
                        DomainExpression.Property("Score"),
                        DomainExpression.Literal(0)))
                .Apply(session: session));

        var response = EvolveTool.Remove(sessionId, "policy",
            """{"entityName":"Order","name":"Guard","stageName":"Active"}""");

        await Assert.That(response.Success).IsTrue();
        var desc = OracleTool.DescribeDomainElement(sessionId, "policy", "Guard", entityName: "Order");
        await Assert.That(desc.Success).IsFalse();
        await Assert.That(desc.Message).Contains("not found");
    }

    [Test]
    public async Task Remove_Policy_ActionScope_Succeeds() {
        // B3: action-scoped policy removal via optional actionName payload field.
        var (sessionId, _) = McpSessionStore.Create("UnifiedRemoveTest");
        EvolveTool.Add(sessionId, "entity", """{"name":"Order"}""");
        EvolveTool.Add(sessionId, "action", """{"entityName":"Order","name":"Submit"}""");
        EvolveTool.Add(sessionId, "property", """{"entityName":"Order","name":"Score","typeName":"Number"}""");

        McpSessionStore.Evolve(sessionId, (domain, session) =>
            new DomainEvolution(domain).Evolve()
                .AddPolicyToAction("Order", "Submit", "Guard",
                    DomainExpression.GreaterThanOrEqual(
                        DomainExpression.Property("Score"),
                        DomainExpression.Literal(0)))
                .Apply(session: session));

        var response = EvolveTool.Remove(sessionId, "policy",
            """{"entityName":"Order","name":"Guard","actionName":"Submit"}""");

        await Assert.That(response.Success).IsTrue();
        var desc = OracleTool.DescribeDomainElement(sessionId, "policy", "Guard", entityName: "Order");
        await Assert.That(desc.Success).IsFalse();
        await Assert.That(desc.Message).Contains("not found");
    }

    [Test]
    public async Task Remove_Policy_BothScopes_Fails() {
        // B3: providing both stageName and actionName is ambiguous — fail closed.
        var (sessionId, _) = McpSessionStore.Create("UnifiedRemoveTest");
        EvolveTool.Add(sessionId, "entity", """{"name":"Order"}""");

        var response = EvolveTool.Remove(sessionId, "policy",
            """{"entityName":"Order","name":"Guard","stageName":"Active","actionName":"Submit"}""");

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("at most one");
    }

    [Test]
    public async Task Remove_MissingSession_InvalidJson_ReportsSessionFirst() {
        // S6: session check precedes payload parse.
        var response = EvolveTool.Remove("nonexistent", "entity", "not json");

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("not found");
        await Assert.That(response.Message).DoesNotContain("payload");
    }
}