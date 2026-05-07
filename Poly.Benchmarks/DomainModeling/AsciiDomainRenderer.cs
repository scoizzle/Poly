using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data.Modeling;
using Poly.Data.Modeling.Analysis;
using Poly.Data.Modeling.Effects;
using Poly.Syntax.Analysis;

using DomainAction = Poly.Data.Modeling.Action;

namespace Poly.Benchmarks.DomainModeling;

internal static class AsciiDomainRenderer {
    public static string Render(Domain domain) {
        var sb = new StringBuilder();

        sb.AppendLine("===============================================================");
        sb.AppendLine($" DOMAIN: {domain.Name}");
        sb.AppendLine("===============================================================");
        sb.AppendLine();

        RenderPrimitives(sb, domain);
        RenderEntities(sb, domain);
        RenderRelationships(sb, domain);

        return sb.ToString();
    }

    public static string RenderEntitySummary(Entity entity) {
        ArgumentNullException.ThrowIfNull(entity);

        var sb = new StringBuilder();
        sb.AppendLine("===============================================================");
        sb.AppendLine($" ENTITY SUMMARY: {entity.Name}");
        sb.AppendLine("===============================================================");
        sb.AppendLine();

        RenderEntity(sb, entity);
        return sb.ToString();
    }

    public static string RenderStageSummary(Stage stage) {
        ArgumentNullException.ThrowIfNull(stage);

        var analysis = new DomainModelAnalyzer().Analyze(stage.Domain);
        var capability = analysis.GetCapabilityView(stage);
        var localActionNames = stage.Actions.Select(action => action.Name).ToHashSet(StringComparer.Ordinal);
        var localPolicyNames = stage.Policies.Select(policy => policy.Name).ToHashSet(StringComparer.Ordinal);

        var effectiveActions = capability.EffectiveActions
            .Select(action => localActionNames.Contains(action.ActionName)
                ? action.ActionName
                : $"{action.ActionName} [inherited]")
            .ToArray();

        var effectivePolicies = capability.EffectivePolicies
            .Select(policy => localPolicyNames.Contains(policy.Name)
                ? policy.Name
                : $"{policy.Name} [inherited]")
            .ToArray();

        var sb = new StringBuilder();
        sb.AppendLine("===============================================================");
        sb.AppendLine($" STAGE SUMMARY: {stage.Name}");
        sb.AppendLine("===============================================================");
        sb.AppendLine();

        DrawBox(sb, [
            $"Stage: {stage.Name}",
            $"Parent: {stage.Parent?.Name ?? "(none)"}",
            $"Actions: {FormatNameList(effectiveActions)}",
            $"Policies: {FormatNameList(effectivePolicies)}"
        ]);

        sb.AppendLine();
        return sb.ToString();
    }

    private static void RenderPrimitives(StringBuilder sb, Domain domain) {
        var primitives = domain.GetAvailablePrimitives().OrderBy(p => p.Name).ToArray();
        sb.AppendLine("[PRIMITIVES]");

        if (primitives.Length == 0) {
            sb.AppendLine("  (none)");
            sb.AppendLine();
            return;
        }

        foreach (var primitive in primitives) {
            sb.AppendLine($"  - {primitive.Name} ({primitive.Category})");
        }

        sb.AppendLine();
    }

    private static void RenderEntities(StringBuilder sb, Domain domain) {
        sb.AppendLine("[ENTITIES]");

        var entities = domain.GetAvailableEntities().Where(entity => entity is not Relationship).OrderBy(entity => entity.Name).ToArray();
        if (entities.Length == 0) {
            sb.AppendLine("  (none)");
            sb.AppendLine();
            return;
        }

        foreach (var entity in entities) {
            RenderEntity(sb, entity);
        }
    }

    private static void RenderEntity(StringBuilder sb, Entity entity) {
        var effectiveProperties = GetEffectiveProperties(entity);
        var effectiveStages = GetEffectiveStages(entity);
        var effectiveEvents = GetEffectiveEvents(entity);
        var effectiveActions = GetEffectiveActions(entity);

        var lines = new List<string> {
            $"Entity: {entity.Name}",
            $"Parent: {entity.ParentEntity?.Name ?? "(none)"}",
            $"Properties: {FormatInheritedPropertyList(effectiveProperties, entity)}",
            $"Stages: {FormatInheritedNameList(effectiveStages, entity, stage => stage.Name)}",
            $"Events: {FormatInheritedNameList(effectiveEvents, entity, @event => @event.Name)}"
        };

        lines.Add("Actions:");
        foreach (var (action, owner) in effectiveActions.OrderBy(entry => entry.Item.Name)) {
            var inheritedMarker = ReferenceEquals(owner, entity) ? string.Empty : " [inherited]";
            lines.Add($"  - {action.Name}{inheritedMarker} | Params: {FormatPropertyList(action.Parameters.OfType<Property>())} | Effects: {FormatEffects(action)}");
        }

        DrawBox(sb, lines);

        foreach (var (@event, owner) in effectiveEvents.OrderBy(entry => entry.Item.Name)) {
            var inheritedMarker = ReferenceEquals(owner, entity) ? string.Empty : " [inherited]";
            sb.AppendLine($"    Event {@event.Name}{inheritedMarker}: {FormatPropertyList(@event.Properties)}");
        }

        sb.AppendLine();
    }

    private static List<(T Item, Entity Owner)> GetEffectiveByName<T>(Entity entity, Func<Entity, IEnumerable<T>> selector, Func<T, string> keySelector) {
        var result = new Dictionary<string, (T Item, Entity Owner)>(StringComparer.Ordinal);

        for (var current = entity; current is not null; current = current.ParentEntity) {
            foreach (var item in selector(current)) {
                var key = keySelector(item);
                _ = result.TryAdd(key, (item, current));
            }
        }

        return result.Values.ToList();
    }

    private static List<(Property Item, Entity Owner)> GetEffectiveProperties(Entity entity) {
        return GetEffectiveByName(entity, owner => owner.Properties, property => property.Name);
    }

    private static List<(Stage Item, Entity Owner)> GetEffectiveStages(Entity entity) {
        return GetEffectiveByName(entity, owner => owner.Stages, stage => stage.Name);
    }

    private static List<(Event Item, Entity Owner)> GetEffectiveEvents(Entity entity) {
        return GetEffectiveByName(entity, owner => owner.Events, @event => @event.Name);
    }

    private static List<(DomainAction Item, Entity Owner)> GetEffectiveActions(Entity entity) {
        return GetEffectiveByName(entity, owner => owner.Actions, action => action.Name);
    }

    private static void RenderRelationships(StringBuilder sb, Domain domain) {
        sb.AppendLine("[RELATIONSHIPS]");

        var relationships = domain.Relationships.OrderBy(relationship => relationship.Name).ToArray();
        if (relationships.Length == 0) {
            sb.AppendLine("  (none)");
            return;
        }

        foreach (var relationship in relationships) {
            sb.AppendLine($"  {relationship.Source.Name} --[{relationship.Name}:{relationship.Cardinality}]--> {relationship.Target.Name}");
            sb.AppendLine($"    SourceOwnsTarget: {relationship.SourceOwnsTarget}");
            sb.AppendLine($"    Properties: {FormatPropertyList(relationship.Properties)}");
            sb.AppendLine($"    Stages: {FormatNameList(relationship.Stages.Select(stage => stage.Name))}");
            sb.AppendLine($"    Policies: {FormatNameList(relationship.Policies.Select(policy => policy.Name))}");
        }
    }

    private static string FormatEffects(DomainAction action) {
        if (action.Effects.Count == 0) {
            return "(none)";
        }

        var descriptions = new List<string>();
        foreach (var effect in action.Effects) {
            switch (effect) {
                case StageTransition stageTransition:
                    descriptions.Add($"StageTransition->{stageTransition.TargetStage.Name}");
                    break;
                case PublishEvent publishEvent:
                    descriptions.Add($"PublishEvent:{publishEvent.Event.Name}");
                    break;
                case CreateEntityInstance createEntityInstance:
                    descriptions.Add($"CreateEntity:{createEntityInstance.EntityType.Name}@{createEntityInstance.InitialStage?.Name ?? "(default)"}");
                    break;
                case InvokeAction invokeAction:
                    descriptions.Add($"InvokeAction:{invokeAction.TargetAction.Name}");
                    break;
                default:
                    descriptions.Add(effect.GetType().Name);
                    break;
            }
        }

        return string.Join(", ", descriptions);
    }

    private static string FormatPropertyList(IEnumerable<Property> properties) {
        var pairs = properties.Select(property => $"{property.Name}:{property.Type.Name}").ToArray();
        return pairs.Length == 0 ? "(none)" : string.Join(", ", pairs);
    }

    private static string FormatInheritedPropertyList(IEnumerable<(Property Item, Entity Owner)> properties, Entity selectedEntity) {
        var values = properties
            .OrderBy(entry => entry.Item.Name)
            .Select(entry => {
                var inherited = ReferenceEquals(entry.Owner, selectedEntity) ? string.Empty : " [inherited]";
                return $"{entry.Item.Name}:{entry.Item.Type.Name}{inherited}";
            })
            .ToArray();

        return values.Length == 0 ? "(none)" : string.Join(", ", values);
    }

    private static string FormatInheritedNameList<T>(
        IEnumerable<(T Item, Entity Owner)> values,
        Entity selectedEntity,
        Func<T, string> nameSelector) {
        var names = values
            .OrderBy(entry => nameSelector(entry.Item))
            .Select(entry => {
                var inherited = ReferenceEquals(entry.Owner, selectedEntity) ? string.Empty : " [inherited]";
                return $"{nameSelector(entry.Item)}{inherited}";
            })
            .ToArray();

        return names.Length == 0 ? "(none)" : string.Join(", ", names);
    }

    private static string FormatNameList(IEnumerable<string> values) {
        var names = values.ToArray();
        return names.Length == 0 ? "(none)" : string.Join(", ", names);
    }

    private static void DrawBox(StringBuilder sb, IEnumerable<string> lines) {
        const int width = 74;
        var horizontal = "+" + new string('-', width - 2) + "+";
        sb.AppendLine(horizontal);

        foreach (var line in lines) {
            var clipped = line.Length > width - 4 ? line[..(width - 7)] + "..." : line;
            sb.AppendLine($"| {clipped.PadRight(width - 4)} |");
        }

        sb.AppendLine(horizontal);
    }
}