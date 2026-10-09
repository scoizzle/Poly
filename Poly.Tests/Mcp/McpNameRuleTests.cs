using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.Mcp.Sessions;
using Poly.Mcp.Tools;

namespace Poly.Tests.Mcp;

/// <summary>
/// MCP <c>add</c> and <c>create_domain_session</c> accept a new name exactly when the DSL parser would.
/// </summary>
public class McpNameRuleTests {
    private const string PropertyTemplate =
        "domain D\n\nMoney: value {\n  Amount: Number\n}\n\nOther: entity {\n  N: Text required\n}\n\nE: entity {\n  NAME: TYPE\n}\n";
    private const string StageTemplate = "domain D\n\nE: entity {\n  NAME: stage { }\n}\n";
    private const string EntityTemplate = "domain D\n\nNAME: entity {\n  N: Text required\n}\n";
    private const string DomainTemplate = "domain NAME\n\nE: entity {\n  N: Text required\n}\n";

    // Valid and invalid under the DSL scanner: digits first, punctuation, spaces, and keywords.
    private static readonly string[] Names = [
        "Order", "_x", "a1", "Café", "Order_Item",
        "1Order", "Order-Item", "A.B", "A B", "A/B", "A#B", "stage", "length", "entity", "default", "equals", "",
    ];

    // Text, Number and Boolean are keywords the parser still accepts as a property name of a primitive type.
    private static readonly string[] PrimitiveKeywords = ["Text", "Number", "Boolean"];

    private static bool Parses(string template, string name) {
        var sessionId = SessionTool.CreateDomainSession("Probe").SessionId!;
        return DslTool.ApplyDsl(sessionId, template.Replace("NAME", name)).Success;
    }

    [Test]
    [MethodDataSource(nameof(IdentifierCases))]
    public async Task IsIdentifier_AgreesWithTheDslParser(string template, string name) {
        var parsed = Parses(template, name);

        await Assert.That(DslTokenReader.IsIdentifier(name)).IsEqualTo(parsed);
    }

    public static IEnumerable<Func<(string, string)>> IdentifierCases() =>
        from template in new[] { EntityTemplate, StageTemplate, DomainTemplate }
        from name in Names.Concat(PrimitiveKeywords)
        select (Func<(string, string)>)(() => (template, name));

    [Test]
    [MethodDataSource(nameof(PropertyNameCases))]
    public async Task IsPropertyName_AgreesWithTheDslParser(string name, string typeName) {
        var sessionId = SessionTool.CreateDomainSession("Probe").SessionId!;
        McpSessionStore.TryGet(sessionId, out var state);
        var primitives = state.Domain.Types.OfType<PrimitiveType>().Select(t => t.Name).ToHashSet();

        var parsed = DslTool.ApplyDsl(sessionId, PropertyTemplate.Replace("NAME", name).Replace("TYPE", typeName)).Success;

        await Assert.That(DslTokenReader.IsPropertyName(name, typeName, primitives.Contains)).IsEqualTo(parsed);
    }

    // Primitive keyword types, a known primitive name (Date), a value type and an entity.
    public static IEnumerable<Func<(string, string)>> PropertyNameCases() =>
        from name in Names.Concat(PrimitiveKeywords)
        from typeName in new[] { "Text", "Number", "Boolean", "Date", "Money", "Other" }
        select (Func<(string, string)>)(() => (name, typeName));

    [Test]
    [Arguments("1Domain")]
    [Arguments("My-Domain")]
    [Arguments("stage")]
    public async Task CreateDomainSession_WithNameTheDslRefuses_CreatesNoSession(string name) {
        var response = SessionTool.CreateDomainSession(name);

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("is not a valid name");
        await Assert.That(response.SessionId).IsNull();
        // Other tests create sessions in parallel, so look for this name rather than count sessions.
        var created = McpSessionStore.ListSessions()
            .Any(id => McpSessionStore.TryGet(id, out var state) && state.Domain.Name == name);
        await Assert.That(created).IsFalse();
    }

    [Test]
    [Arguments("entity", """{"name":"BAD"}""")]
    [Arguments("property", """{"entityName":"Order","name":"BAD","typeName":"Number"}""")]
    [Arguments("stage", """{"entityName":"Order","name":"BAD"}""")]
    [Arguments("action", """{"entityName":"Order","name":"BAD"}""")]
    [Arguments("stage_action", """{"entityName":"Order","stageName":"Draft","name":"BAD"}""")]
    [Arguments("relationship", """{"name":"BAD","source":"Order","target":"Order"}""")]
    [Arguments("policy", """{"entityName":"Order","name":"BAD","expression":"true"}""")]
    [Arguments("value_type", """{"name":"BAD"}""")]
    [Arguments("contract", """{"name":"BAD","source":"s","version":"1"}""")]
    [Arguments("contract_value_type", """{"contractName":"C","name":"BAD"}""")]
    [Arguments("contract_endpoint", """{"contractName":"C","name":"BAD","payloadType":"Number"}""")]
    [Arguments("contract_binding", """{"name":"BAD","contractName":"C","endpointName":"E","actionName":"A","parameter":"p"}""")]
    public async Task Add_WithNameTheDslRefuses_ChangesNothing(string kind, string payloadTemplate) {
        foreach (var bad in new[] { "1st", "Order-Item", "A B", "stage" }) {
            var (sessionId, _) = McpSessionStore.Create("NameRule");
            EvolveTool.Add(sessionId, "entity", """{"name":"Order"}""");
            EvolveTool.Add(sessionId, "stage", """{"entityName":"Order","name":"Draft"}""");
            McpSessionStore.TryGet(sessionId, out var before);

            var response = EvolveTool.Add(sessionId, kind, payloadTemplate.Replace("BAD", bad));

            await Assert.That(response.Success).IsFalse();
            await Assert.That(response.Message).Contains("is not a valid name").And.Contains(bad);
            McpSessionStore.TryGet(sessionId, out var after);
            await Assert.That(after.Revision).IsEqualTo(before.Revision);
            await Assert.That(ReferenceEquals(after.Domain, before.Domain)).IsTrue();
        }
    }

    [Test]
    public async Task Add_PropertyAndStage_WithNameTheDslRefuses_LeaveTheEntityAsItWas() {
        var (sessionId, _) = McpSessionStore.Create("NameRule");
        EvolveTool.Add(sessionId, "entity", """{"name":"Order"}""");

        EvolveTool.Add(sessionId, "property", """{"entityName":"Order","name":"Total-Price","typeName":"Number"}""");
        EvolveTool.Add(sessionId, "stage", """{"entityName":"Order","name":"1st"}""");

        var data = (EntityDetailData)QueryTool.GetEntityDetail(sessionId, "Order").Data!;
        await Assert.That(data.Properties).IsEmpty();
        await Assert.That(data.Stages).IsEmpty();
    }

    [Test]
    [Arguments("Text", "Number")]
    [Arguments("Number", "Text")]
    [Arguments("Boolean", "Boolean")]
    [Arguments("Text", "Date")]
    public async Task Add_Property_NamedAfterAPrimitiveKeyword_WithAPrimitiveType_SucceedsAndReapplies(string name, string typeName) {
        var (sessionId, _) = McpSessionStore.Create("NameRule");
        EvolveTool.Add(sessionId, "entity", """{"name":"Order"}""");

        var response = EvolveTool.Add(sessionId, "property", $$"""{"entityName":"Order","name":"{{name}}","typeName":"{{typeName}}"}""");

        await Assert.That(response.Success).IsTrue();
        var exported = DslTool.ExportDsl(sessionId).Data!;
        var poly = (string)exported.GetType().GetProperty("poly")!.GetValue(exported)!;
        var reapplied = DslTool.ApplyDsl(SessionTool.CreateDomainSession("Reapply").SessionId!, poly);
        await Assert.That(reapplied.Success).IsTrue();
    }

    [Test]
    [Arguments("Text", "Money")]
    [Arguments("Number", "Line")]
    public async Task Add_Property_NamedAfterAPrimitiveKeyword_WithAValueTypeOrEntity_IsRefused(string name, string typeName) {
        var (sessionId, _) = McpSessionStore.Create("NameRule");
        EvolveTool.Add(sessionId, "entity", """{"name":"Order"}""");
        EvolveTool.Add(sessionId, "entity", """{"name":"Line"}""");
        EvolveTool.Add(sessionId, "value_type", """{"name":"Money"}""");
        McpSessionStore.TryGet(sessionId, out var before);

        var response = EvolveTool.Add(sessionId, "property", $$"""{"entityName":"Order","name":"{{name}}","typeName":"{{typeName}}"}""");

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("is not a valid name");
        McpSessionStore.TryGet(sessionId, out var after);
        await Assert.That(after.Revision).IsEqualTo(before.Revision);
    }

    [Test]
    public async Task NameStartingWithUnderscore_IsAcceptedByAddAndByTheDsl() {
        var (sessionId, _) = McpSessionStore.Create("NameRule");

        var response = EvolveTool.Add(sessionId, "entity", """{"name":"_Draft"}""");

        await Assert.That(response.Success).IsTrue();
        await Assert.That(Parses(EntityTemplate, "_Draft")).IsTrue();
    }
}
