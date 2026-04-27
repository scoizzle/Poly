using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data.Modeling;
using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.TypeSystem;

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

        var capability = stage.GetCapabilityView();
        var sb = new StringBuilder();
        sb.AppendLine("===============================================================");
        sb.AppendLine($" STAGE SUMMARY: {stage.Name}");
        sb.AppendLine("===============================================================");
        sb.AppendLine();

        DrawBox(sb, [
            $"Stage: {stage.Name}",
            $"Parent: {stage.Parent?.Name ?? "(none)"}",
            $"Local Actions: {FormatNameList(capability.LocalActions.Select(action => action.ActionName))}",
            $"Effective Actions: {FormatNameList(capability.EffectiveActions.Select(action => action.ActionName))}",
            $"Local Policies: {FormatNameList(capability.LocalPolicies.Select(policy => policy.Name))}",
            $"Effective Policies: {FormatNameList(capability.EffectivePolicies.Select(policy => policy.Name))}"
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
        var lines = new List<string> {
            $"Entity: {entity.Name}",
            $"Parent: {entity.ParentEntity?.Name ?? "(none)"}",
            $"Properties: {FormatPropertyList(entity.Properties)}",
            $"Stages: {FormatNameList(entity.Stages.Select(stage => stage.Name))}",
            $"Events: {FormatNameList(entity.Events.Select(@event => @event.Name))}",
            $"Actions: {FormatNameList(entity.Actions.Select(action => action.Name))}"
        };

        DrawBox(sb, lines);

        foreach (var action in entity.Actions.OrderBy(action => action.Name)) {
            sb.AppendLine($"    Action {action.Name}");
            sb.AppendLine($"      Params: {FormatPropertyList(action.Parameters.OfType<Property>())}");
            sb.AppendLine($"      Effects: {FormatEffects(action)}");
        }

        foreach (var @event in entity.Events.OrderBy(@event => @event.Name)) {
            sb.AppendLine($"    Event {@event.Name}: {FormatPropertyList(@event.Properties)}");
        }

        sb.AppendLine();
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