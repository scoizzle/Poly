using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

public sealed class DomainMutationIntentEngine {
    internal static TNode ResolveNode<TNode>(Domain domain, DomainNodeReference reference) where TNode : DomainMember {
        ArgumentNullException.ThrowIfNull(reference);

        var segments = reference.Path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length == 0) {
            throw new ArgumentException("Node path cannot be null or empty.", nameof(reference));
        }

        var current = domain as DomainMember;

        foreach (var segment in segments) {
            var next = current.ChildObjects.OfType<DomainMember>().FirstOrDefault(child => child != null && string.Equals(child.Name, segment, StringComparison.Ordinal));

            if (next is null)
                throw new InvalidOperationException($"Could not resolve path '{reference.Path}' - segment '{segment}' not found under '{current.Name}'.");

            current = next;
        }

        if (current is not TNode typedNode) {
            throw new InvalidOperationException($"Could not resolve path '{reference.Path}' as {typeof(TNode).Name}.");
        }

        return typedNode;
    }

    public AnalysisResult Apply(Domain domain, DomainMutationIntent intent, DomainModelAnalyzer? analyzer = null, AnalysisResult? preMutationAnalysis = null) {
        ArgumentNullException.ThrowIfNull(intent);
        return Apply(domain, [intent], analyzer, preMutationAnalysis);
    }

    public AnalysisResult Apply(Domain domain, IEnumerable<DomainMutationIntent> intents, DomainModelAnalyzer? analyzer = null, AnalysisResult? preMutationAnalysis = null) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(intents);

        var mutation = domain.CreateMutation(analyzer);

        foreach (var intent in intents) {
            ArgumentNullException.ThrowIfNull(intent);

            switch (intent) {
                case SetDomainNameIntent setDomainName:
                    _ = mutation.SetDomainName(setDomainName.Name);
                    break;

                case AddPrimitiveTypeIntent addPrimitiveType:
                    _ = mutation.AddType(new Primitive(domain, addPrimitiveType.Name, addPrimitiveType.Category));
                    break;

                case AddEntityTypeIntent addEntityType:
                    _ = mutation.AddType(CreateEntity(domain, addEntityType));
                    break;

                case AddEventTypeIntent addEventType:
                    _ = mutation.AddType(new Event(domain, addEventType.Name));
                    break;

                case RemoveTypeIntent removeType: {
                        var type = domain.Types.FirstOrDefault(t => string.Equals(t.Name, removeType.Name, StringComparison.Ordinal))
                            ?? throw new InvalidOperationException($"Type '{removeType.Name}' was not found in domain '{domain.Name}'.");
                        _ = mutation.RemoveType(type);
                        break;
                    }

                case AddRelationshipIntent addRelationship: {
                        var rel = CreateRelationship(domain, addRelationship);
                        _ = mutation.AddRelationship(rel).AddEntityRelationship(rel.Source, rel);
                        break;
                    }

                case RemoveRelationshipIntent removeRelationship: {
                        var rel = domain.RequireRelationship(removeRelationship.Name);
                        _ = mutation.RemoveEntityRelationship(rel.Source, rel).RemoveRelationship(rel);
                        break;
                    }

                case SetRelationshipShapeIntent setShape: {
                        var rel = domain.RequireRelationship(setShape.RelationshipName);
                        var source = ResolveNode<Entity>(domain, setShape.Source);
                        var target = ResolveNode<Entity>(domain, setShape.Target);
                        _ = mutation.SetRelationship(rel, source, target, setShape.Cardinality, setShape.SourceOwnsTarget);
                        break;
                    }

                case AddPropertyToEntityIntent addProp: {
                        var entity = domain.RequireEntity(addProp.EntityName);
                        var type = RequireType(domain, addProp.TypeName);
                        _ = mutation.AddProperty(entity, new Property(domain, addProp.PropertyName, type));
                        break;
                    }

                case RemovePropertyFromEntityIntent removeProp: {
                        var entity = domain.RequireEntity(removeProp.EntityName);
                        var property = entity.RequireProperty(removeProp.PropertyName);
                        _ = mutation.RemoveProperty(entity, property);
                        break;
                    }

                case AddStageToEntityIntent addStage: {
                        var entity = domain.RequireEntity(addStage.EntityName);
                        var parent = string.IsNullOrWhiteSpace(addStage.ParentStageName) ? null : entity.RequireStage(addStage.ParentStageName);
                        var stage = new Stage(domain, addStage.StageName) { Parent = parent };
                        _ = mutation.AddStage(entity, stage);
                        break;
                    }

                case RemoveStageFromEntityIntent removeStage: {
                        var entity = domain.RequireEntity(removeStage.EntityName);
                        var stage = entity.RequireStage(removeStage.StageName);
                        _ = mutation.RemoveStage(entity, stage);
                        break;
                    }

                case AddActionToEntityIntent addAction: {
                        var entity = domain.RequireEntity(addAction.EntityName);
                        var action = new Action(domain, addAction.ActionName, entity);
                        _ = mutation.AddAction(entity, action);
                        break;
                    }

                case RemoveActionFromEntityIntent removeAction: {
                        var entity = domain.RequireEntity(removeAction.EntityName);
                        var action = entity.RequireAction(removeAction.ActionName);
                        _ = mutation.RemoveAction(entity, action);
                        break;
                    }

                case AddEventToEntityIntent addEvent: {
                        var entity = domain.RequireEntity(addEvent.EntityName);
                        var eventType = domain.RequireEventType(addEvent.EventTypeName);
                        _ = mutation.AddEvent(entity, eventType);
                        break;
                    }

                case RemoveEventFromEntityIntent removeEvent: {
                        var entity = domain.RequireEntity(removeEvent.EntityName);
                        var @event = entity.RequireEvent(removeEvent.EventTypeName);
                        _ = mutation.RemoveEvent(entity, @event);
                        break;
                    }

                case AddPropertyToEventTypeIntent addEventProp: {
                        var eventType = domain.RequireEventType(addEventProp.EventTypeName);
                        var type = RequireType(domain, addEventProp.TypeName);
                        _ = mutation.AddProperty(eventType, new Property(domain, addEventProp.PropertyName, type));
                        break;
                    }

                case RemovePropertyFromEventTypeIntent removeEventProp: {
                        var eventType = domain.RequireEventType(removeEventProp.EventTypeName);
                        var property = eventType.RequireProperty(removeEventProp.PropertyName);
                        _ = mutation.RemoveProperty(eventType, property);
                        break;
                    }

                case AddActionToStageIntent addStageAction: {
                        var entity = domain.RequireEntity(addStageAction.EntityName);
                        var stage = entity.RequireStage(addStageAction.StageName);
                        var action = entity.RequireAction(addStageAction.ActionName);
                        _ = mutation.AddAction(stage, action);
                        break;
                    }

                case RemoveActionFromStageIntent removeStageAction: {
                        var entity = domain.RequireEntity(removeStageAction.EntityName);
                        var stage = entity.RequireStage(removeStageAction.StageName);
                        var action = stage.RequireAction(removeStageAction.ActionName);
                        _ = mutation.RemoveAction(stage, action);
                        break;
                    }

                case AddActionParameterIntent addParam: {
                        var entity = domain.RequireEntity(addParam.EntityName);
                        var action = entity.RequireAction(addParam.ActionName);
                        var type = RequireType(domain, addParam.TypeName);
                        _ = mutation.AddParameter(action, new Property(domain, addParam.ParameterName, type));
                        break;
                    }

                case RemoveActionParameterIntent removeParam: {
                        var entity = domain.RequireEntity(removeParam.EntityName);
                        var action = entity.RequireAction(removeParam.ActionName);
                        var parameter = action.RequireParameter(removeParam.ParameterName);
                        _ = mutation.RemoveParameter(action, parameter);
                        break;
                    }

                default:
                    throw new NotSupportedException($"Unsupported domain mutation intent '{intent.GetType().Name}'.");
            }
        }

        return mutation.Apply(preMutationAnalysis);
    }

    private static DomainType RequireType(Domain domain, string typeName) {
        return domain.Types.FirstOrDefault(t => string.Equals(t.Name, typeName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Type '{typeName}' was not found in domain '{domain.Name}'.");
    }

    private static Entity CreateEntity(Domain domain, AddEntityTypeIntent intent) {
        var parent = intent.ParentEntity is null
            ? null
            : ResolveNode<Entity>(domain, intent.ParentEntity);

        return new Entity(domain, intent.Name, parent);
    }

    private static Relationship CreateRelationship(Domain domain, AddRelationshipIntent intent) {
        var source = ResolveNode<Entity>(domain, intent.SourceEntity);
        var target = ResolveNode<Entity>(domain, intent.TargetEntity);

        return new Relationship(domain, intent.Name, source, target, intent.Cardinality, intent.SourceOwnsTarget);
    }
}