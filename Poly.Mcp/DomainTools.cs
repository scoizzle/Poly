using System.Collections.Concurrent;
using System.ComponentModel;

using ModelContextProtocol.Server;

using Poly.Data.Modeling;
using Poly.Data.Modeling.TypeSystem;
using Poly.Introspection;
using Poly.Syntax.Analysis;

namespace Poly.Mcp;

internal static class DomainSessionStore {
    private static readonly ConcurrentDictionary<string, (Domain Domain, AnalysisResult? LatestAnalysis)> Sessions = new(StringComparer.Ordinal);

    public static (string SessionId, Domain Domain) Create(string domainName, string? preferredSessionId = null) {
        var sessionId = string.IsNullOrWhiteSpace(preferredSessionId)
            ? Guid.NewGuid().ToString("N")
            : preferredSessionId;

        var domain = new Domain(domainName);
        Sessions[sessionId] = (domain, default);
        return (sessionId, domain);
    }

    public static bool TryGet(string sessionId, out (Domain Domain, AnalysisResult? LatestAnalysis) session) {
        if (string.IsNullOrWhiteSpace(sessionId)) {
            session = (null!, null);
            return false;
        }

        if (!Sessions.TryGetValue(sessionId, out var value)) {
            session = (null!, null);
            return false;
        }

        session = value;
        return true;
    }

    public static void UpdateAnalysis(string sessionId, AnalysisResult analysis) {
        if (string.IsNullOrWhiteSpace(sessionId)) {
            throw new ArgumentException("Session ID is required.", nameof(sessionId));
        }

        if (!Sessions.TryGetValue(sessionId, out var value)) {
            throw new InvalidOperationException($"Session '{sessionId}' was not found.");
        }

        Sessions[sessionId] = (value.Domain, analysis);
    }

    public static IReadOnlyCollection<string> ListSessionIds() => Sessions.Keys.OrderBy(static id => id, StringComparer.Ordinal).ToArray();
}

public sealed record DomainMutationRequest(
    string Operation,
    string? SessionId = null,
    string? DomainName = null,
    string? EntityName = null,
    string? ParentEntityName = null,
    string? PrimitiveName = null,
    string? EventName = null,
    string? PropertyName = null,
    string? TypeName = null,
    string? StageName = null,
    string? ParentStageName = null,
    string? ActionName = null,
    string? RelationshipName = null,
    string? SourceEntityName = null,
    string? TargetEntityName = null,
    string? Cardinality = null,
    bool? SourceOwnsTarget = null,
    string? PrimitiveCategory = null);

public sealed record DomainMutationResponse(
    bool Success,
    string Message,
    string? SessionId,
    string? DomainName,
    DomainSnapshot? Snapshot,
    IReadOnlyCollection<string>? Diagnostics = null);

public sealed record DomainCapabilityResponse(
    bool Success,
    string Message,
    string? SessionId,
    IReadOnlyCollection<string> AvailableSessions,
    IReadOnlyCollection<string> SupportedMutationOperations,
    DomainSnapshot? Snapshot);

public sealed record DomainSnapshot(
    string DomainName,
    IReadOnlyCollection<PrimitiveSnapshot> Primitives,
    IReadOnlyCollection<EntitySnapshot> Entities,
    IReadOnlyCollection<EventTypeSnapshot> EventTypes,
    IReadOnlyCollection<RelationshipSnapshot> Relationships);

public sealed record PrimitiveSnapshot(string Name, string Category);

public sealed record EventTypeSnapshot(string Name, IReadOnlyCollection<PropertySnapshot> Properties);

public sealed record EntitySnapshot(
    string Name,
    string? ParentEntityName,
    IReadOnlyCollection<PropertySnapshot> Properties,
    IReadOnlyCollection<string> Events,
    IReadOnlyCollection<ActionCapabilitySnapshot> Actions,
    IReadOnlyCollection<StageCapabilitySnapshot> Stages,
    IReadOnlyCollection<string> Relationships);

public sealed record PropertySnapshot(string Name, string TypeName);

public sealed record ActionCapabilitySnapshot(
    string Name,
    IReadOnlyCollection<string> ParameterNames,
    IReadOnlyCollection<string> EffectTypes,
    IReadOnlyCollection<string> PublishedEvents,
    IReadOnlyCollection<string> TransitionTargets);

public sealed record StageCapabilitySnapshot(
    string Name,
    IReadOnlyCollection<string> LocalActions,
    IReadOnlyCollection<string> EffectiveActions,
    IReadOnlyCollection<string> LocalPolicies,
    IReadOnlyCollection<string> EffectivePolicies);

public sealed record RelationshipSnapshot(
    string Name,
    string Source,
    string Target,
    string Cardinality,
    bool SourceOwnsTarget,
    IReadOnlyCollection<PropertySnapshot> Properties,
    IReadOnlyCollection<string> Stages,
    IReadOnlyCollection<string> Policies);

[McpServerToolType]
public static class DomainCapabilityTool {
    private static readonly string[] SupportedMutationOperations = [
        "create_domain",
        "set_domain_name",
        "add_entity",
        "add_primitive",
        "add_event_type",
        "add_property_to_entity",
        "add_property_to_event_type",
        "add_stage_to_entity",
        "add_action_to_entity",
        "add_event_to_entity",
        "add_relationship"
    ];

    [McpServerTool, Description("Interrogates Poly domain capabilities and returns a summary of the active domain session.")]
    public static DomainCapabilityResponse InterrogateDomainCapabilities(string? sessionId = null) {
        if (string.IsNullOrWhiteSpace(sessionId)) {
            return new DomainCapabilityResponse(
                Success: true,
                Message: "No session selected. Returning available sessions and supported mutations.",
                SessionId: null,
                AvailableSessions: DomainSessionStore.ListSessionIds(),
                SupportedMutationOperations: SupportedMutationOperations,
                Snapshot: null);
        }

        if (!DomainSessionStore.TryGet(sessionId, out (Domain Domain, AnalysisResult? LatestAnalysis) session)) {
            return new DomainCapabilityResponse(
                Success: false,
                Message: $"Session '{sessionId}' was not found.",
                SessionId: sessionId,
                AvailableSessions: DomainSessionStore.ListSessionIds(),
                SupportedMutationOperations: SupportedMutationOperations,
                Snapshot: null);
        }

        return new DomainCapabilityResponse(
            Success: true,
            Message: "Domain capability snapshot generated.",
            SessionId: sessionId,
            AvailableSessions: DomainSessionStore.ListSessionIds(),
            SupportedMutationOperations: SupportedMutationOperations,
            Snapshot: BuildSnapshot(session.Domain));
    }

    internal static DomainSnapshot BuildSnapshot(Domain domain) {
        var primitives = domain.GetAvailablePrimitives()
            .OrderBy(static primitive => primitive.Name, StringComparer.Ordinal)
            .Select(static primitive => new PrimitiveSnapshot(primitive.Name, primitive.Category.ToString()))
            .ToArray();

        var eventTypes = domain.GetAvailableEventTypes()
            .OrderBy(static @event => @event.Name, StringComparer.Ordinal)
            .Select(static @event => new EventTypeSnapshot(
                Name: @event.Name,
                Properties: @event.Properties
                    .Select(static property => new PropertySnapshot(property.Name, property.Type.Name))
                    .OrderBy(static property => property.Name, StringComparer.Ordinal)
                    .ToArray()))
            .ToArray();

        var entities = domain.GetAvailableEntities()
            .OrderBy(static entity => entity.Name, StringComparer.Ordinal)
            .Select(static entity => new EntitySnapshot(
                Name: entity.Name,
                ParentEntityName: entity.ParentEntity?.Name,
                Properties: entity.Properties
                    .Select(static property => new PropertySnapshot(property.Name, property.Type.Name))
                    .OrderBy(static property => property.Name, StringComparer.Ordinal)
                    .ToArray(),
                Events: entity.Events
                    .Select(static @event => @event.Name)
                    .OrderBy(static name => name, StringComparer.Ordinal)
                    .ToArray(),
                Actions: entity.Actions
                    .OrderBy(static action => action.Name, StringComparer.Ordinal)
                    .Select(static action => action.GetCapabilityView())
                    .Select(static action => new ActionCapabilitySnapshot(
                        Name: action.ActionName,
                        ParameterNames: action.Parameters
                            .Select(static parameter => parameter.Name)
                            .OrderBy(static name => name, StringComparer.Ordinal)
                            .ToArray(),
                        EffectTypes: action.EffectTypes
                            .Select(static effectType => effectType.Name)
                            .OrderBy(static name => name, StringComparer.Ordinal)
                            .ToArray(),
                        PublishedEvents: action.PublishedEvents
                            .Select(static @event => @event.Name)
                            .OrderBy(static name => name, StringComparer.Ordinal)
                            .ToArray(),
                        TransitionTargets: action.TransitionTargets
                            .Select(static stage => stage.Name)
                            .OrderBy(static name => name, StringComparer.Ordinal)
                            .ToArray()))
                    .ToArray(),
                Stages: entity.Stages
                    .OrderBy(static stage => stage.Name, StringComparer.Ordinal)
                    .Select(static stage => stage.GetCapabilityView())
                    .Select(static stage => new StageCapabilitySnapshot(
                        Name: stage.StageName,
                        LocalActions: stage.LocalActions
                            .Select(static action => action.ActionName)
                            .OrderBy(static name => name, StringComparer.Ordinal)
                            .ToArray(),
                        EffectiveActions: stage.EffectiveActions
                            .Select(static action => action.ActionName)
                            .OrderBy(static name => name, StringComparer.Ordinal)
                            .ToArray(),
                        LocalPolicies: stage.LocalPolicies
                            .Select(static policy => policy.Name)
                            .OrderBy(static name => name, StringComparer.Ordinal)
                            .ToArray(),
                        EffectivePolicies: stage.EffectivePolicies
                            .Select(static policy => policy.Name)
                            .OrderBy(static name => name, StringComparer.Ordinal)
                            .ToArray()))
                    .ToArray(),
                Relationships: entity.Relationships
                    .Select(static relationship => relationship.Name)
                    .OrderBy(static name => name, StringComparer.Ordinal)
                    .ToArray()))
            .ToArray();

        var relationships = domain.GetAvailableRelationships()
            .OrderBy(static relationship => relationship.Name, StringComparer.Ordinal)
            .Select(static relationship => {
                var view = relationship.GetCapabilityView();
                return new RelationshipSnapshot(
                    Name: view.RelationshipName,
                    Source: view.Source.Name,
                    Target: view.Target.Name,
                    Cardinality: view.Cardinality.ToString(),
                    SourceOwnsTarget: view.SourceOwnsTarget,
                    Properties: view.Properties
                        .Select(static property => new PropertySnapshot(property.Name, property.Type.Name))
                        .OrderBy(static property => property.Name, StringComparer.Ordinal)
                        .ToArray(),
                    Stages: view.Stages
                        .Select(static stage => stage.Name)
                        .OrderBy(static name => name, StringComparer.Ordinal)
                        .ToArray(),
                    Policies: view.Policies
                        .Select(static policy => policy.Name)
                        .OrderBy(static name => name, StringComparer.Ordinal)
                        .ToArray());
            })
            .ToArray();

        return new DomainSnapshot(
            DomainName: domain.Name,
            Primitives: primitives,
            Entities: entities,
            EventTypes: eventTypes,
            Relationships: relationships);
    }
}

[McpServerToolType]
public static class DomainAuthoringTool {
    [McpServerTool, Description("Applies a domain authoring or mutation operation to a Poly domain session.")]
    public static DomainMutationResponse ApplyDomainMutation(DomainMutationRequest request) {
        ArgumentNullException.ThrowIfNull(request);

        try {
            var operation = NormalizeOperation(request.Operation);

            if (string.Equals(operation, "create_domain", StringComparison.Ordinal)) {
                var domainName = RequireValue(request.DomainName, nameof(request.DomainName));
                var (sessionId, createdDomain) = DomainSessionStore.Create(domainName, request.SessionId);

                return new DomainMutationResponse(
                    Success: true,
                    Message: $"Created domain '{domainName}' in session '{sessionId}'.",
                    SessionId: sessionId,
                    DomainName: createdDomain?.Name,
                    Snapshot: createdDomain is null ? null : DomainCapabilityTool.BuildSnapshot(createdDomain));
            }

            var sessionIdForMutation = RequireValue(request.SessionId, nameof(request.SessionId));
            if (!DomainSessionStore.TryGet(sessionIdForMutation, out (Domain Domain, AnalysisResult? LatestAnalysis) session)) {
                return new DomainMutationResponse(
                    Success: false,
                    Message: $"Session '{sessionIdForMutation}' was not found.",
                    SessionId: sessionIdForMutation,
                    DomainName: null,
                    Snapshot: null);
            }

            var existingDomain = session.Domain;
            var mutation = existingDomain.CreateMutation();

            switch (operation) {
                case "set_domain_name":
                    _ = mutation.SetDomainName(RequireValue(request.DomainName, nameof(request.DomainName)));
                    break;

                case "add_entity": {
                        var entityName = RequireValue(request.EntityName, nameof(request.EntityName));
                        var parentEntity = string.IsNullOrWhiteSpace(request.ParentEntityName)
                            ? null
                            : existingDomain.RequireEntity(request.ParentEntityName);
                        var entity = new Entity(existingDomain, entityName, parentEntity);
                        _ = mutation.AddType(entity);
                        break;
                    }

                case "add_primitive": {
                        var primitiveName = RequireValue(request.PrimitiveName, nameof(request.PrimitiveName));
                        var category = ParseTypeCategory(request.PrimitiveCategory);
                        var primitive = new Primitive(existingDomain, primitiveName, category);
                        _ = mutation.AddType(primitive);
                        break;
                    }

                case "add_event_type": {
                        var eventName = RequireValue(request.EventName, nameof(request.EventName));
                        var eventType = new Event(existingDomain, eventName);
                        _ = mutation.AddType(eventType);
                        break;
                    }

                case "add_property_to_entity": {
                        var entity = existingDomain.RequireEntity(RequireValue(request.EntityName, nameof(request.EntityName)));
                        var propertyName = RequireValue(request.PropertyName, nameof(request.PropertyName));
                        var propertyType = ResolveType(existingDomain, RequireValue(request.TypeName, nameof(request.TypeName)));
                        var property = new Property(existingDomain, propertyName, propertyType);
                        _ = mutation.AddProperty(entity, property);
                        break;
                    }

                case "add_property_to_event_type": {
                        var eventType = existingDomain.RequireEventType(RequireValue(request.EventName, nameof(request.EventName)));
                        var propertyName = RequireValue(request.PropertyName, nameof(request.PropertyName));
                        var propertyType = ResolveType(existingDomain, RequireValue(request.TypeName, nameof(request.TypeName)));
                        var property = new Property(existingDomain, propertyName, propertyType);
                        _ = mutation.AddProperty(eventType, property);
                        break;
                    }

                case "add_stage_to_entity": {
                        var entity = existingDomain.RequireEntity(RequireValue(request.EntityName, nameof(request.EntityName)));
                        var stageName = RequireValue(request.StageName, nameof(request.StageName));
                        var parentStage = string.IsNullOrWhiteSpace(request.ParentStageName)
                            ? null
                            : entity.RequireStage(request.ParentStageName);
                        var stage = new Stage(existingDomain, stageName) {
                            Parent = parentStage
                        };
                        _ = mutation.AddStage(entity, stage);
                        break;
                    }

                case "add_action_to_entity": {
                        var entity = existingDomain.RequireEntity(RequireValue(request.EntityName, nameof(request.EntityName)));
                        var actionName = RequireValue(request.ActionName, nameof(request.ActionName));
                        var action = new Poly.Data.Modeling.Action(existingDomain, actionName, entity);
                        _ = mutation.AddAction(entity, action);
                        break;
                    }

                case "add_event_to_entity": {
                        var entity = existingDomain.RequireEntity(RequireValue(request.EntityName, nameof(request.EntityName)));
                        var eventType = existingDomain.RequireEventType(RequireValue(request.EventName, nameof(request.EventName)));
                        _ = mutation.AddEvent(entity, eventType);
                        break;
                    }

                case "add_relationship": {
                        var relationshipName = RequireValue(request.RelationshipName, nameof(request.RelationshipName));
                        var source = existingDomain.RequireEntity(RequireValue(request.SourceEntityName, nameof(request.SourceEntityName)));
                        var target = existingDomain.RequireEntity(RequireValue(request.TargetEntityName, nameof(request.TargetEntityName)));
                        var cardinality = ParseCardinality(request.Cardinality);
                        var sourceOwnsTarget = request.SourceOwnsTarget ?? false;
                        var relationship = new Relationship(existingDomain, relationshipName, source, target, cardinality, sourceOwnsTarget);

                        _ = mutation.AddRelationship(relationship);
                        _ = mutation.AddEntityRelationship(source, relationship);
                        break;
                    }

                default:
                    return new DomainMutationResponse(
                        Success: false,
                        Message: $"Unsupported operation '{request.Operation}'.",
                        SessionId: sessionIdForMutation,
                        DomainName: existingDomain.Name,
                        Snapshot: DomainCapabilityTool.BuildSnapshot(existingDomain));
            }

            var analysis = mutation.Apply(session.LatestAnalysis);
            var diagnostics = analysis.Diagnostics
                .Select(static diagnostic => $"{diagnostic.Severity}: {diagnostic.Code} - {diagnostic.Message}")
                .ToArray();

            DomainSessionStore.UpdateAnalysis(sessionIdForMutation, analysis);

            return new DomainMutationResponse(
                Success: true,
                Message: $"Operation '{operation}' applied successfully.",
                SessionId: sessionIdForMutation,
                DomainName: existingDomain.Name,
                Snapshot: DomainCapabilityTool.BuildSnapshot(existingDomain),
                Diagnostics: diagnostics);
        }
        catch (Exception ex) {
            return new DomainMutationResponse(
                Success: false,
                Message: ex.Message,
                SessionId: request.SessionId,
                DomainName: null,
                Snapshot: null,
                Diagnostics: [ex.ToString()]);
        }
    }

    private static string NormalizeOperation(string operation) {
        var normalized = RequireValue(operation, nameof(operation)).Trim().ToLowerInvariant();
        return normalized.Replace('-', '_');
    }

    private static string RequireValue(string? value, string paramName) {
        if (string.IsNullOrWhiteSpace(value)) {
            throw new ArgumentException($"'{paramName}' is required.", paramName);
        }

        return value;
    }

    private static RelationshipCardinality ParseCardinality(string? value) {
        var input = string.IsNullOrWhiteSpace(value) ? nameof(RelationshipCardinality.OneToMany) : value;
        if (Enum.TryParse<RelationshipCardinality>(input, ignoreCase: true, out var parsed)) {
            return parsed;
        }

        throw new InvalidOperationException($"Unknown relationship cardinality '{value}'.");
    }

    private static TypeCategory ParseTypeCategory(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return TypeCategory.Primitive;
        }

        if (Enum.TryParse<TypeCategory>(value, ignoreCase: true, out var parsed)) {
            return parsed;
        }

        throw new InvalidOperationException($"Unknown primitive category '{value}'.");
    }

    private static IDomainType ResolveType(Domain domain, string typeName) {
        foreach (var type in domain.Types) {
            if (string.Equals(type.Name, typeName, StringComparison.Ordinal)) {
                return type;
            }
        }

        throw new InvalidOperationException($"Type '{typeName}' was not found in domain '{domain.Name}'.");
    }
}