using System.Text.Json;

using Poly.Mcp;

namespace Poly.Tests.Mcp;

public class DomainMcpToolsTests {
    [Test]
    public async Task DomainSessionStore_UpdateAnalysis_ConcurrentCallsAdvanceRevisionsAtomically() {
        var sessionId = $"mcp-concurrency-{Guid.NewGuid():N}";
        _ = DomainAuthoringTool.CreateDomain("Concurrent", sessionId);
        const int workerCount = 24;
        using var ready = new CountdownEvent(workerCount);
        using var start = new ManualResetEventSlim(false);

        var tasks = Enumerable.Range(0, workerCount)
            .Select(_ => Task.Run(() => {
                ready.Signal();
                start.Wait();
                return DomainOperabilityTool.GetDomainAnalysis(sessionId).Revision;
            }))
            .ToArray();

        ready.Wait();
        start.Set();

        var revisions = await Task.WhenAll(tasks);

        await Assert.That(revisions.Any(static revision => revision is null)).IsFalse();
        await Assert.That(revisions.Distinct().Count()).IsEqualTo(workerCount);
        await Assert.That(revisions.Max()).IsEqualTo(workerCount);
    }

    [Test]
    public async Task CreateDomain_ReturnsSessionAndAffordances() {
        var sessionId = $"mcp-test-{Guid.NewGuid():N}";

        var response = DomainAuthoringTool.CreateDomain("Orders", sessionId);

        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.SessionId).IsEqualTo(sessionId);
        await Assert.That(response.DomainName).IsEqualTo("Orders");
        await Assert.That(response.Affordances.Select(item => item.Tool)).Contains(nameof(DomainQueryTool.GetDomainOverview));
    }

    [Test]
    public async Task CreateDomain_SeedsCanonicalBuiltInPrimitiveTypes() {
        var sessionId = $"mcp-test-{Guid.NewGuid():N}";
        _ = DomainAuthoringTool.CreateDomain("Canonical", sessionId);

        var primitives = DomainQueryTool.ListPrimitives(sessionId);

        await Assert.That(primitives.Success).IsTrue();
        await Assert.That(primitives.Data).IsNotNull();

        var names = primitives.Data!.Select(static item => item.Name).ToArray();
        await Assert.That(names).Contains("Boolean");
        await Assert.That(names).Contains("Number");
        await Assert.That(names).Contains("Text");
        await Assert.That(names).Contains("DateTime");
        await Assert.That(names).Contains("Uuid");

        var booleanType = primitives.Data!.First(static primitive => primitive.Name == "Boolean");
        await Assert.That(booleanType.IsRequired).IsFalse();
        await Assert.That(booleanType.IsNullable).IsTrue();
    }

    [Test]
    public async Task AddPrimitive_WithNullableCategory_ReturnsFailed() {
        var sessionId = $"mcp-test-{Guid.NewGuid():N}";
        _ = DomainAuthoringTool.CreateDomain("PrimitiveValidation", sessionId);

        var response = DomainAuthoringTool.AddPrimitive(sessionId, "MaybeText", "Text, Nullable");

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("RequiredConstraint");
    }

    [Test]
    public async Task AddPrimitive_WithCollectionCategory_ReturnsFailed() {
        var sessionId = $"mcp-test-{Guid.NewGuid():N}";
        _ = DomainAuthoringTool.CreateDomain("PrimitiveValidation", sessionId);

        var response = DomainAuthoringTool.AddPrimitive(sessionId, "TextListLike", "Text, Collection");

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("relationships");
    }

    [Test]
    public async Task AddEnumConstraints_PropertyLevelOverridesTypeLevel_InEntityQuery() {
        var sessionId = $"mcp-test-{Guid.NewGuid():N}";

        _ = DomainAuthoringTool.CreateDomain("EnumOverride", sessionId);
        _ = DomainAuthoringTool.AddPrimitive(sessionId, "StatusType", "Text");
        _ = DomainAuthoringTool.AddEntity(sessionId, "Task");
        _ = DomainAuthoringTool.AddPropertyToEntity(sessionId, "Task", "Status", "StatusType");
        _ = DomainAuthoringTool.AddPropertyToEntity(sessionId, "Task", "Lifecycle", "StatusType");

        var typeConstraint = DomainAuthoringTool.AddEnumConstraintToType(sessionId, "StatusType", [
            new EnumMemberDto("Open", 1, "Open Label"),
            new EnumMemberDto("Closed", 2, "Closed Label")
        ]);

        var propertyConstraint = DomainAuthoringTool.AddEnumConstraintToEntityProperty(sessionId, "Task", "Status", [
            new EnumMemberDto("Draft", 10, "Draft Label")
        ]);

        var entity = DomainQueryTool.GetEntity(sessionId, "Task");

        await Assert.That(typeConstraint.Success).IsTrue();
        await Assert.That(propertyConstraint.Success).IsTrue();
        await Assert.That(entity.Success).IsTrue();
        await Assert.That(entity.Data).IsNotNull();

        var status = entity.Data!.Properties.First(static property => property.Name == "Status");
        await Assert.That(status.LocalEnumMembers.Select(static member => member.Name)).IsEquivalentTo(["Draft"]);
        await Assert.That(status.EffectiveEnumMembers.Select(static member => member.Name)).IsEquivalentTo(["Draft"]);

        var lifecycle = entity.Data.Properties.First(static property => property.Name == "Lifecycle");
        await Assert.That(lifecycle.LocalEnumMembers.Count).IsEqualTo(0);
        await Assert.That(lifecycle.EffectiveEnumMembers.Select(static member => member.Name)).IsEquivalentTo(["Open", "Closed"]);
    }

    [Test]
    public async Task ExportImportDomainSession_RoundTripsCoreDomainModel() {
        var sourceSessionId = $"mcp-test-{Guid.NewGuid():N}";

        _ = DomainAuthoringTool.CreateDomain("Persisted", sourceSessionId);
        _ = DomainAuthoringTool.AddPrimitive(sourceSessionId, "StatusType", "Text");
        _ = DomainAuthoringTool.AddEntity(sourceSessionId, "Project");
        _ = DomainAuthoringTool.AddEntity(sourceSessionId, "Task");
        _ = DomainAuthoringTool.AddPropertyToEntity(sourceSessionId, "Task", "Status", "StatusType");
        _ = DomainAuthoringTool.AddEnumConstraintToType(sourceSessionId, "StatusType", [
            new EnumMemberDto("Open", 1, "Open Label"),
            new EnumMemberDto("Closed", 2, "Closed Label")
        ]);
        _ = DomainAuthoringTool.AddRelationship(sourceSessionId, "ProjectTasks", "Project", "Task", "OneToMany", sourceOwnsTarget: true);

        var exported = DomainAuthoringTool.ExportDomainSession(sourceSessionId);

        await Assert.That(exported.Success).IsTrue();
        await Assert.That(exported.Data).IsNotNull();

        var importedSessionId = $"mcp-import-{Guid.NewGuid():N}";
        var imported = DomainAuthoringTool.ImportDomainSession(exported.Data!, importedSessionId);

        await Assert.That(imported.Success).IsTrue();
        await Assert.That(imported.SessionId).IsEqualTo(importedSessionId);

        var importedEntity = DomainQueryTool.GetEntity(importedSessionId, "Task");
        var importedRelationships = DomainQueryTool.ListRelationships(importedSessionId);

        await Assert.That(importedEntity.Success).IsTrue();
        await Assert.That(importedEntity.Data).IsNotNull();
        await Assert.That(importedRelationships.Success).IsTrue();
        await Assert.That(importedRelationships.Data).IsNotNull();

        var status = importedEntity.Data!.Properties.First(static property => property.Name == "Status");
        await Assert.That(status.EffectiveEnumMembers.Select(static member => member.Name)).IsEquivalentTo(["Open", "Closed"]);
        await Assert.That(importedRelationships.Data!.Select(static relationship => relationship.Name)).Contains("ProjectTasks");
    }

    [Test]
    public async Task AddEntityAndProperty_IterativeQueryReturnsEntityDetails() {
        var sessionId = $"mcp-test-{Guid.NewGuid():N}";

        _ = DomainAuthoringTool.CreateDomain("Catalog", sessionId);
        _ = DomainAuthoringTool.AddEntity(sessionId, "Product");
        var propertyResponse = DomainAuthoringTool.AddPropertyToEntity(sessionId, "Product", "Name", "Text");
        var entityQuery = DomainQueryTool.GetEntity(sessionId, "Product");

        await Assert.That(propertyResponse.Success).IsTrue();
        await Assert.That(entityQuery.Success).IsTrue();
        await Assert.That(entityQuery.Data).IsNotNull();

        var product = entityQuery.Data!;
        await Assert.That(product.Name).IsEqualTo("Product");
        await Assert.That(product.Properties.Select(property => property.Name)).Contains("Name");
    }

    [Test]
    public async Task AddPropertyToEntity_UnknownTypeName_ReturnsFailed() {
        var sessionId = $"mcp-test-{Guid.NewGuid():N}";

        _ = DomainAuthoringTool.CreateDomain("WrappedProperty", sessionId);
        _ = DomainAuthoringTool.AddEntity(sessionId, "Task");

        var propertyResponse = DomainAuthoringTool.AddPropertyToEntity(sessionId, "Task", "DueDate", "UnknownType");

        await Assert.That(propertyResponse.Success).IsFalse();
    }

    [Test]
    public async Task AddParameterToAction_CanonicalTypeName_AddsParameter() {
        var sessionId = $"mcp-test-{Guid.NewGuid():N}";

        _ = DomainAuthoringTool.CreateDomain("WrappedParameter", sessionId);
        _ = DomainAuthoringTool.AddEntity(sessionId, "Task");
        _ = DomainAuthoringTool.AddActionToEntity(sessionId, "Task", "Assign");

        var parameterResponse = DomainAuthoringTool.AddParameterToAction(sessionId, "Task", "Assign", "AssigneeId", "Uuid");
        var entityQuery = DomainQueryTool.GetEntity(sessionId, "Task");

        await Assert.That(parameterResponse.Success).IsTrue();
        await Assert.That(entityQuery.Success).IsTrue();
        await Assert.That(entityQuery.Data).IsNotNull();

        var assign = entityQuery.Data!.Actions.FirstOrDefault(static action => action.Name == "Assign");
        await Assert.That(assign is not null).IsTrue();
        await Assert.That(assign!.ParameterNames).Contains("AssigneeId");
    }

    [Test]
    public async Task AddRelationship_IterativeQueriesExposeRelationshipAndEntityReference() {
        var sessionId = $"mcp-test-{Guid.NewGuid():N}";

        _ = DomainAuthoringTool.CreateDomain("Sales", sessionId);
        _ = DomainAuthoringTool.AddEntity(sessionId, "Customer");
        _ = DomainAuthoringTool.AddEntity(sessionId, "Order");

        var relationshipResponse = DomainAuthoringTool.AddRelationship(
            sessionId, "CustomerOrders", "Customer", "Order", "OneToMany", sourceOwnsTarget: true);
        var relationshipQuery = DomainQueryTool.GetRelationship(sessionId, "CustomerOrders");
        var customerQuery = DomainQueryTool.GetEntity(sessionId, "Customer", includeActions: false, includeStages: false);

        await Assert.That(relationshipResponse.Success).IsTrue();
        await Assert.That(relationshipQuery.Success).IsTrue();
        await Assert.That(relationshipQuery.Data).IsNotNull();
        await Assert.That(customerQuery.Success).IsTrue();
        await Assert.That(customerQuery.Data).IsNotNull();

        var relationship = relationshipQuery.Data!;
        await Assert.That(relationship.Source).IsEqualTo("Customer");
        await Assert.That(relationship.Target).IsEqualTo("Order");
        await Assert.That(relationship.SourceOwnsTarget).IsTrue();

        var customer = customerQuery.Data!;
        await Assert.That(customer.Relationships).Contains("CustomerOrders");
    }

    [Test]
    public async Task InterrogateDomainCapabilities_WithoutSession_ReturnsSuccess() {
        var response = DomainCapabilityTool.InterrogateDomainCapabilities();

        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Overview).IsNull();
        await Assert.That(response.Affordances.Select(item => item.Tool)).Contains(nameof(DomainAuthoringTool.CreateDomain));
    }

    [Test]
    public async Task GetDomainAnalysis_ReturnsTelemetrySummary() {
        var sessionId = $"mcp-test-{Guid.NewGuid():N}";
        _ = DomainAuthoringTool.CreateDomain("Health", sessionId);
        _ = DomainAuthoringTool.AddPrimitive(sessionId, "string", "Text");

        var response = DomainOperabilityTool.GetDomainAnalysis(sessionId);

        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Data).IsNotNull();
        await Assert.That(response.Data!.Passes.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task DiffDomainRevision_ReturnsChangesBetweenRevisions() {
        var sessionId = $"mcp-test-{Guid.NewGuid():N}";
        _ = DomainAuthoringTool.CreateDomain("Diff", sessionId);
        _ = DomainAuthoringTool.AddEntity(sessionId, "Customer");
        _ = DomainAuthoringTool.AddEntity(sessionId, "Order");

        var response = DomainOperabilityTool.DiffDomainRevision(sessionId, fromRevision: 0);

        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Data).IsNotNull();
        await Assert.That(response.Data!.AddedCount).IsGreaterThan(0);
    }

    [Test]
    public async Task ApplyMutationWithTrace_ReturnsTraceForSupportedMutation() {
        var sessionId = $"mcp-test-{Guid.NewGuid():N}";
        _ = DomainAuthoringTool.CreateDomain("Trace", sessionId);

        var response = DomainOperabilityTool.ApplyMutationWithTrace(
            sessionId,
            mutationType: "AddEntity",
            name: "Ticket");

        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Data).IsNotNull();
        await Assert.That(response.Data!.AppliedStepCount).IsGreaterThan(0);
        await Assert.That(response.Data.Steps.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task ApplyMutationWithTrace_UnsupportedMutation_ReturnsFailure() {
        var sessionId = $"mcp-test-{Guid.NewGuid():N}";
        _ = DomainAuthoringTool.CreateDomain("TraceFail", sessionId);

        var response = DomainOperabilityTool.ApplyMutationWithTrace(
            sessionId,
            mutationType: "Nope",
            name: "unused");

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Diagnostics).IsNotNull();
        await Assert.That(response.Diagnostics!.Any(static diagnostic => diagnostic.Contains("code=UNSUPPORTED_MUTATION", StringComparison.Ordinal))).IsTrue();
        await Assert.That(response.Diagnostics!.Any(static diagnostic => diagnostic.Contains("category=InvalidArgument", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task GetLoweredInterpretationAst_EntitySelection_ReturnsLoweredTypeTree() {
        var sessionId = $"mcp-test-{Guid.NewGuid():N}";
        _ = DomainAuthoringTool.CreateDomain("Lowering", sessionId);
        _ = DomainAuthoringTool.AddEntity(sessionId, "Task");
        _ = DomainAuthoringTool.AddPropertyToEntity(sessionId, "Task", "Name", "Text");
        _ = DomainAuthoringTool.AddActionToEntity(sessionId, "Task", "Rename");
        _ = DomainAuthoringTool.AddParameterToAction(sessionId, "Task", "Rename", "newName", "Text");

        var response = DomainOperabilityTool.GetLoweredInterpretationAst(sessionId, nodePath: "Task");

        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Data).IsNotNull();
        await Assert.That(response.Data!.SelectionKind).IsEqualTo("EntityTypeDefinition");
        await Assert.That(response.Data.SourceNodeType).IsEqualTo("Entity");
        await Assert.That(response.Data.SourceNodePath).IsEqualTo("Task");
        await Assert.That(response.Data.Roots.Select(static root => root.Kind)).Contains("TypeDefinitionNode");

        var typeRoot = response.Data.Roots.First(static root => root.Kind == "TypeDefinitionNode");
        await Assert.That(typeRoot.Properties["Name"]).IsEqualTo("Task");
        await Assert.That(typeRoot.Children.Select(static child => child.Node.Kind)).Contains("MethodDefinitionNode");
    }

    [Test]
    public async Task GenerateLoweredCSharp_ActionSelection_ReturnsMethodSource() {
        var sessionId = $"mcp-test-{Guid.NewGuid():N}";
        _ = DomainAuthoringTool.CreateDomain("Lowering", sessionId);
        _ = DomainAuthoringTool.AddEntity(sessionId, "Task");
        _ = DomainAuthoringTool.AddActionToEntity(sessionId, "Task", "Rename");
        _ = DomainAuthoringTool.AddParameterToAction(sessionId, "Task", "Rename", "newName", "Text");

        var response = DomainOperabilityTool.GenerateLoweredCSharp(sessionId, nodePath: "Task/Rename");

        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Data).IsNotNull();
        await Assert.That(response.Data!.SelectionKind).IsEqualTo("ActionMethodDefinition");
        await Assert.That(response.Data.SourceNodeType).IsEqualTo("Action");
        await Assert.That(response.Data.Code).Contains("Result Rename(ActionExecutionContext context, String newName)");
        await Assert.That(response.Data.Code).Contains("return Result.Success();");
    }

    [Test]
    public async Task GetLoweredInterpretationAst_MissingSession_ReturnsStableNotFoundDiagnostics() {
        var missingSession = $"missing-{Guid.NewGuid():N}";

        var response = DomainOperabilityTool.GetLoweredInterpretationAst(missingSession, nodePath: "Task");

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Diagnostics).IsNotNull();
        await Assert.That(response.Diagnostics!.Any(static diagnostic => diagnostic.Contains("code=SESSION_NOT_FOUND", StringComparison.Ordinal))).IsTrue();
        await Assert.That(response.Diagnostics!.Any(static diagnostic => diagnostic.Contains("category=NotFound", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task DiffDomainRevision_WhenOldRevisionPruned_ReturnsFailure() {
        var sessionId = $"mcp-test-{Guid.NewGuid():N}";
        _ = DomainAuthoringTool.CreateDomain("Retention", sessionId);

        for (var i = 0; i < 80; i++) {
            _ = DomainOperabilityTool.ApplyMutationWithTrace(
                sessionId,
                mutationType: "SetDomainName",
                name: $"Retention-{i}");
        }

        var response = DomainOperabilityTool.DiffDomainRevision(sessionId, fromRevision: 0);

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("Revision '0' was not found");
        await Assert.That(response.Diagnostics).IsNotNull();
        await Assert.That(response.Diagnostics!.Any(static diagnostic => diagnostic.Contains("code=REVISION_NOT_FOUND", StringComparison.Ordinal))).IsTrue();
        await Assert.That(response.Diagnostics!.Any(static diagnostic => diagnostic.Contains("category=NotFound", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task OperabilityEndpoints_MissingSession_ReturnStableNotFoundDiagnostics() {
        var missingSession = $"missing-{Guid.NewGuid():N}";

        var analysis = DomainOperabilityTool.GetDomainAnalysis(missingSession);
        var diff = DomainOperabilityTool.DiffDomainRevision(missingSession, fromRevision: 0);
        var mutation = DomainOperabilityTool.ApplyMutationWithTrace(missingSession, "SetDomainName", "anything");

        await Assert.That(analysis.Success).IsFalse();
        await Assert.That(diff.Success).IsFalse();
        await Assert.That(mutation.Success).IsFalse();

        await Assert.That(analysis.Diagnostics).IsNotNull();
        await Assert.That(diff.Diagnostics).IsNotNull();
        await Assert.That(mutation.Diagnostics).IsNotNull();

        await Assert.That(analysis.Diagnostics!.Any(static diagnostic => diagnostic.Contains("code=SESSION_NOT_FOUND", StringComparison.Ordinal))).IsTrue();
        await Assert.That(diff.Diagnostics!.Any(static diagnostic => diagnostic.Contains("code=SESSION_NOT_FOUND", StringComparison.Ordinal))).IsTrue();
        await Assert.That(mutation.Diagnostics!.Any(static diagnostic => diagnostic.Contains("code=SESSION_NOT_FOUND", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task GetDomainAnalysis_SerializationContract_RemainsStable() {
        var sessionId = $"mcp-test-{Guid.NewGuid():N}";
        _ = DomainAuthoringTool.CreateDomain("HealthContract", sessionId);

        var response = DomainOperabilityTool.GetDomainAnalysis(sessionId);
        var json = JsonSerializer.Serialize(response);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        await Assert.That(HasProperty(root, "success")).IsTrue();
        await Assert.That(HasProperty(root, "message")).IsTrue();
        await Assert.That(HasProperty(root, "sessionId")).IsTrue();
        await Assert.That(HasProperty(root, "revision")).IsTrue();
        await Assert.That(HasProperty(root, "data")).IsTrue();

        var data = root.GetProperty("Data");
        await Assert.That(HasProperty(data, "hasErrors")).IsTrue();
        await Assert.That(HasProperty(data, "errorCount")).IsTrue();
        await Assert.That(HasProperty(data, "warningCount")).IsTrue();
        await Assert.That(HasProperty(data, "totalAnalysisTime")).IsTrue();
        await Assert.That(HasProperty(data, "incremental")).IsTrue();
        await Assert.That(HasProperty(data, "passes")).IsTrue();
    }

    [Test]
    public async Task DiffDomainRevision_SerializationContract_RemainsStable() {
        var sessionId = $"mcp-test-{Guid.NewGuid():N}";
        _ = DomainAuthoringTool.CreateDomain("DiffContract", sessionId);
        _ = DomainAuthoringTool.AddEntity(sessionId, "Customer");

        var response = DomainOperabilityTool.DiffDomainRevision(sessionId, fromRevision: 0);
        var json = JsonSerializer.Serialize(response);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        await Assert.That(HasProperty(root, "data")).IsTrue();
        var data = root.GetProperty("Data");
        await Assert.That(HasProperty(data, "fromRevision")).IsTrue();
        await Assert.That(HasProperty(data, "toRevision")).IsTrue();
        await Assert.That(HasProperty(data, "addedCount")).IsTrue();
        await Assert.That(HasProperty(data, "removedCount")).IsTrue();
        await Assert.That(HasProperty(data, "changedCount")).IsTrue();
        await Assert.That(HasProperty(data, "added")).IsTrue();
        await Assert.That(HasProperty(data, "removed")).IsTrue();
        await Assert.That(HasProperty(data, "changed")).IsTrue();
    }

    [Test]
    public async Task ApplyMutationWithTrace_SerializationContract_RemainsStable() {
        var sessionId = $"mcp-test-{Guid.NewGuid():N}";
        _ = DomainAuthoringTool.CreateDomain("TraceContract", sessionId);

        var response = DomainOperabilityTool.ApplyMutationWithTrace(sessionId, "AddEntity", "Ticket");
        var json = JsonSerializer.Serialize(response);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        await Assert.That(HasProperty(root, "data")).IsTrue();
        var data = root.GetProperty("Data");
        await Assert.That(HasProperty(data, "succeeded")).IsTrue();
        await Assert.That(HasProperty(data, "rolledBack")).IsTrue();
        await Assert.That(HasProperty(data, "appliedStepCount")).IsTrue();
        await Assert.That(HasProperty(data, "duration")).IsTrue();
        await Assert.That(HasProperty(data, "errorCount")).IsTrue();
        await Assert.That(HasProperty(data, "warningCount")).IsTrue();
        await Assert.That(HasProperty(data, "affectedNodeIds")).IsTrue();
        await Assert.That(HasProperty(data, "steps")).IsTrue();
        await Assert.That(HasProperty(data, "diagnostics")).IsTrue();
    }

    [Test]
    public async Task GetLoweredInterpretationAst_SerializationContract_RemainsStable() {
        var sessionId = $"mcp-test-{Guid.NewGuid():N}";
        _ = DomainAuthoringTool.CreateDomain("LoweredAstContract", sessionId);
        _ = DomainAuthoringTool.AddEntity(sessionId, "Task");

        var response = DomainOperabilityTool.GetLoweredInterpretationAst(sessionId, nodePath: "Task");
        var json = JsonSerializer.Serialize(response);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        await Assert.That(HasProperty(root, "data")).IsTrue();
        var data = root.GetProperty("Data");
        await Assert.That(HasProperty(data, "selectionKind")).IsTrue();
        await Assert.That(HasProperty(data, "sourceNodeType")).IsTrue();
        await Assert.That(HasProperty(data, "sourceNodeId")).IsTrue();
        await Assert.That(HasProperty(data, "sourceNodePath")).IsTrue();
        await Assert.That(HasProperty(data, "roots")).IsTrue();

        var astRoot = data.GetProperty("Roots")[0];
        await Assert.That(HasProperty(astRoot, "kind")).IsTrue();
        await Assert.That(HasProperty(astRoot, "nodeId")).IsTrue();
        await Assert.That(HasProperty(astRoot, "summary")).IsTrue();
        await Assert.That(HasProperty(astRoot, "properties")).IsTrue();
        await Assert.That(HasProperty(astRoot, "children")).IsTrue();
    }

    [Test]
    public async Task GenerateLoweredCSharp_SerializationContract_RemainsStable() {
        var sessionId = $"mcp-test-{Guid.NewGuid():N}";
        _ = DomainAuthoringTool.CreateDomain("LoweredCSharpContract", sessionId);

        var response = DomainOperabilityTool.GenerateLoweredCSharp(sessionId);
        var json = JsonSerializer.Serialize(response);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        await Assert.That(HasProperty(root, "data")).IsTrue();
        var data = root.GetProperty("Data");
        await Assert.That(HasProperty(data, "selectionKind")).IsTrue();
        await Assert.That(HasProperty(data, "sourceNodeType")).IsTrue();
        await Assert.That(HasProperty(data, "sourceNodeId")).IsTrue();
        await Assert.That(HasProperty(data, "sourceNodePath")).IsTrue();
        await Assert.That(HasProperty(data, "code")).IsTrue();
    }

    [Test]
    public async Task GetEntity_EnumConstraintSerializationContract_RemainsStable() {
        var sessionId = $"mcp-test-{Guid.NewGuid():N}";
        _ = DomainAuthoringTool.CreateDomain("EnumContract", sessionId);
        _ = DomainAuthoringTool.AddPrimitive(sessionId, "StatusType", "Text");
        _ = DomainAuthoringTool.AddEntity(sessionId, "Task");
        _ = DomainAuthoringTool.AddPropertyToEntity(sessionId, "Task", "Status", "StatusType");
        _ = DomainAuthoringTool.AddEnumConstraintToEntityProperty(sessionId, "Task", "Status", [
            new EnumMemberDto("Draft", 10, "Draft Label")
        ]);

        var response = DomainQueryTool.GetEntity(sessionId, "Task");
        var json = JsonSerializer.Serialize(response);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        await Assert.That(HasProperty(root, "data")).IsTrue();
        var data = root.GetProperty("Data");
        await Assert.That(HasProperty(data, "properties")).IsTrue();

        var firstProperty = data.GetProperty("Properties")[0];
        await Assert.That(HasProperty(firstProperty, "localEnumMembers")).IsTrue();
        await Assert.That(HasProperty(firstProperty, "effectiveEnumMembers")).IsTrue();

        var enumMember = firstProperty.GetProperty("LocalEnumMembers")[0];
        await Assert.That(HasProperty(enumMember, "name")).IsTrue();
        await Assert.That(HasProperty(enumMember, "canonicalValue")).IsTrue();
        await Assert.That(HasProperty(enumMember, "label")).IsTrue();
    }

    [Test]
    public async Task ExportDomainSession_SerializationContract_RemainsStable() {
        var sessionId = $"mcp-test-{Guid.NewGuid():N}";
        _ = DomainAuthoringTool.CreateDomain("ExportContract", sessionId);
        _ = DomainAuthoringTool.AddEntity(sessionId, "Task");

        var response = DomainAuthoringTool.ExportDomainSession(sessionId);
        var json = JsonSerializer.Serialize(response);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        await Assert.That(HasProperty(root, "data")).IsTrue();
        var data = root.GetProperty("Data");
        await Assert.That(HasProperty(data, "domainName")).IsTrue();
        await Assert.That(HasProperty(data, "primitives")).IsTrue();
        await Assert.That(HasProperty(data, "entities")).IsTrue();
        await Assert.That(HasProperty(data, "eventTypes")).IsTrue();
        await Assert.That(HasProperty(data, "relationships")).IsTrue();
    }

    private static bool HasProperty(JsonElement element, string propertyName) {
        foreach (var property in element.EnumerateObject()) {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }
}