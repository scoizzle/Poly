using Poly.Data.Modeling;
using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.TypeSystem;

namespace Poly.DomainModeling.V2;

/// <summary>
/// Converts a live <see cref="Domain"/> into an immutable <see cref="DomainModelSnapshot"/>.
/// </summary>
internal static class SnapshotBuilder {
    internal static DomainModelSnapshot Build(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);

        var primitives = domain.GetAvailablePrimitives()
            .OrderBy(static p => p.Name, StringComparer.Ordinal)
            .Select(static p => new PrimitiveSnapshot(p.Name, p.Category.ToString()))
            .ToArray();

        var entities = domain.GetAvailableEntities()
            .Where(static e => e is not Relationship)
            .OrderBy(static e => e.Name, StringComparer.Ordinal)
            .Select(BuildEntity)
            .ToArray();

        var eventTypes = domain.GetAvailableEventTypes()
            .OrderBy(static e => e.Name, StringComparer.Ordinal)
            .Select(static e => new EventTypeSnapshot(
                e.Name,
                e.Properties.Select(static p => new PropertySnapshot(p.Name, p.Type.Name)).ToArray()))
            .ToArray();

        var relationships = domain.GetAvailableRelationships()
            .OrderBy(static r => r.Name, StringComparer.Ordinal)
            .Select(static r => new RelationshipSnapshot(
                r.Name,
                r.Source.Name,
                r.Target.Name,
                r.Cardinality.ToString(),
                r.SourceOwnsTarget))
            .ToArray();

        return new DomainModelSnapshot(domain.Name, primitives, entities, eventTypes, relationships);
    }

    private static EntitySnapshot BuildEntity(Entity entity) {
        var properties = entity.Properties
            .OrderBy(static p => p.Name, StringComparer.Ordinal)
            .Select(static p => new PropertySnapshot(p.Name, p.Type.Name))
            .ToArray();

        var stages = entity.Stages
            .OrderBy(static s => s.Name, StringComparer.Ordinal)
            .Select(static s => new StageSnapshot(
                s.Name,
                s.Parent?.Name,
                s.Actions.Select(static a => a.Name).OrderBy(static n => n, StringComparer.Ordinal).ToArray()))
            .ToArray();

        var eventNames = entity.Events
            .OrderBy(static e => e.Name, StringComparer.Ordinal)
            .Select(static e => e.Name)
            .ToArray();

        var actions = entity.Actions
            .OrderBy(static a => a.Name, StringComparer.Ordinal)
            .Select(BuildAction)
            .ToArray();

        return new EntitySnapshot(
            entity.Name,
            entity.ParentEntity?.Name,
            properties,
            stages,
            eventNames,
            actions);
    }

    private static ActionSnapshot BuildAction(Data.Modeling.Action action) {
        // Action parameters are always Property instances in the domain model; OfType<Property> is correct here.
        var parameters = action.Parameters.OfType<Data.Modeling.Property>()
            .Select(static p => new PropertySnapshot(p.Name, p.Type.Name))
            .ToArray();

        var effectTypes = action.Effects
            .Select(static e => e.GetType().Name)
            .ToArray();

        var publishedEvents = action.Effects.OfType<PublishEvent>()
            .Select(static e => e.Event.Name)
            .ToArray();

        var transitionTargets = action.Effects.OfType<StageTransition>()
            .Select(static e => e.TargetStage.Name)
            .ToArray();

        return new ActionSnapshot(action.Name, parameters, effectTypes, publishedEvents, transitionTargets);
    }
}
