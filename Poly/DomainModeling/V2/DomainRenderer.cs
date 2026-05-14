using Poly.Data.Modeling;
using Poly.Data.Modeling.Analysis;
using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.TypeSystem;

namespace Poly.DomainModeling.V2;

/// <summary>
/// Provides human-readable ASCII rendering of domain model sessions, entities, and stages.
/// </summary>
public static class DomainRenderer {
    private const int BoxWidth = 74;

    /// <summary>Renders the full domain as an ASCII text report.</summary>
    public static string Render(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);

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

    /// <summary>Renders a single entity as an ASCII text summary.</summary>
    public static string RenderEntitySummary(Entity entity) {
        ArgumentNullException.ThrowIfNull(entity);

        var sb = new StringBuilder();
        sb.AppendLine("===============================================================");
        sb.AppendLine($" ENTITY: {entity.Name}");
        sb.AppendLine("===============================================================");
        sb.AppendLine();

        RenderEntity(sb, entity);
        return sb.ToString();
    }

    /// <summary>
    /// Renders a single stage (with its effective actions and policies) as an ASCII text summary.
    /// Requires the owning domain to be analyzed first.
    /// </summary>
    public static string RenderStageSummary(Stage stage) {
        ArgumentNullException.ThrowIfNull(stage);

        var analysis = new DomainModelAnalyzer().Analyze(stage.Domain);
        var capability = analysis.GetCapabilityView(stage);
        var localActionNames = stage.Actions.Select(static a => a.Name).ToHashSet(StringComparer.Ordinal);
        var localPolicyNames = stage.Policies.Select(static p => p.Name).ToHashSet(StringComparer.Ordinal);

        var effectiveActions = capability.EffectiveActions
            .Select(a => localActionNames.Contains(a.ActionName) ? a.ActionName : $"{a.ActionName} [inherited]")
            .ToArray();

        var effectivePolicies = capability.EffectivePolicies
            .Select(p => localPolicyNames.Contains(p.Name) ? p.Name : $"{p.Name} [inherited]")
            .ToArray();

        var sb = new StringBuilder();
        sb.AppendLine("===============================================================");
        sb.AppendLine($" STAGE: {stage.Name}");
        sb.AppendLine("===============================================================");
        sb.AppendLine();

        DrawBox(sb, [
            $"Stage: {stage.Name}",
            $"Parent: {stage.Parent?.Name ?? "(none)"}",
            $"Actions: {FormatList(effectiveActions)}",
            $"Policies: {FormatList(effectivePolicies)}"
        ]);

        return sb.ToString();
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private static void RenderPrimitives(StringBuilder sb, Domain domain) {
        var primitives = domain.GetAvailablePrimitives().OrderBy(static p => p.Name, StringComparer.Ordinal).ToArray();
        sb.AppendLine("[PRIMITIVES]");

        if (primitives.Length == 0) {
            sb.AppendLine("  (none)");
        }
        else {
            foreach (var p in primitives) {
                sb.AppendLine($"  - {p.Name} ({p.Category})");
            }
        }

        sb.AppendLine();
    }

    private static void RenderEntities(StringBuilder sb, Domain domain) {
        sb.AppendLine("[ENTITIES]");

        var entities = domain.GetAvailableEntities()
            .Where(static e => e is not Relationship)
            .OrderBy(static e => e.Name, StringComparer.Ordinal)
            .ToArray();

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
        var effectiveProperties = GetEffective(entity, static e => e.Properties, static p => p.Name);
        var effectiveStages = GetEffective(entity, static e => e.Stages, static s => s.Name);
        var effectiveEvents = GetEffective(entity, static e => e.Events, static ev => ev.Name);
        var effectiveActions = GetEffective(entity, static e => e.Actions, static a => a.Name);

        var lines = new List<string> {
            $"Entity: {entity.Name}",
            $"Parent: {entity.ParentEntity?.Name ?? "(none)"}",
            $"Properties: {FormatInherited(effectiveProperties, entity, static p => $"{p.Name}:{p.Type.Name}")}",
            $"Stages: {FormatInherited(effectiveStages, entity, static s => s.Name)}",
            $"Events: {FormatInherited(effectiveEvents, entity, static e => e.Name)}"
        };

        lines.Add("Actions:");
        foreach (var (action, owner) in effectiveActions.OrderBy(static x => x.Item.Name, StringComparer.Ordinal)) {
            var marker = ReferenceEquals(owner, entity) ? string.Empty : " [inherited]";
            var parameters = string.Join(", ", action.Parameters.OfType<Data.Modeling.Property>().Select(static p => $"{p.Name}:{p.Type.Name}"));
            lines.Add($"  - {action.Name}{marker} | Params: {(parameters.Length == 0 ? "(none)" : parameters)} | Effects: {DescribeEffects(action)}");
        }

        DrawBox(sb, lines);

        foreach (var (@event, owner) in effectiveEvents.OrderBy(static x => x.Item.Name, StringComparer.Ordinal)) {
            var marker = ReferenceEquals(owner, entity) ? string.Empty : " [inherited]";
            var props = string.Join(", ", @event.Properties.Select(static p => $"{p.Name}:{p.Type.Name}"));
            sb.AppendLine($"    Event {@event.Name}{marker}: {(props.Length == 0 ? "(none)" : props)}");
        }

        sb.AppendLine();
    }

    private static void RenderRelationships(StringBuilder sb, Domain domain) {
        sb.AppendLine("[RELATIONSHIPS]");

        var relationships = domain.GetAvailableRelationships()
            .OrderBy(static r => r.Name, StringComparer.Ordinal)
            .ToArray();

        if (relationships.Length == 0) {
            sb.AppendLine("  (none)");
            return;
        }

        foreach (var r in relationships) {
            sb.AppendLine($"  {r.Source.Name} --[{r.Name}:{r.Cardinality}]--> {r.Target.Name}");
            sb.AppendLine($"    SourceOwnsTarget: {r.SourceOwnsTarget}");
        }
    }

    private static List<(T Item, Entity Owner)> GetEffective<T>(
        Entity entity,
        Func<Entity, IEnumerable<T>> selector,
        Func<T, string> keySelector) {
        var result = new Dictionary<string, (T Item, Entity Owner)>(StringComparer.Ordinal);
        for (var current = entity; current is not null; current = current.ParentEntity) {
            foreach (var item in selector(current)) {
                _ = result.TryAdd(keySelector(item), (item, current));
            }
        }
        return [.. result.Values];
    }

    private static string FormatInherited<T>(
        IEnumerable<(T Item, Entity Owner)> items,
        Entity entity,
        Func<T, string> nameSelector) {
        var parts = items.OrderBy(x => nameSelector(x.Item), StringComparer.Ordinal)
            .Select(x => {
                var marker = ReferenceEquals(x.Owner, entity) ? string.Empty : " [inherited]";
                return $"{nameSelector(x.Item)}{marker}";
            }).ToArray();
        return parts.Length == 0 ? "(none)" : string.Join(", ", parts);
    }

    private static string FormatList(IEnumerable<string> values) {
        var arr = values.ToArray();
        return arr.Length == 0 ? "(none)" : string.Join(", ", arr);
    }

    private static string DescribeEffects(Data.Modeling.Action action) {
        if (action.Effects.Count == 0) return "(none)";

        var parts = new List<string>();
        foreach (var effect in action.Effects) {
            parts.Add(effect switch {
                StageTransition t => $"StageTransition->{t.TargetStage.Name}",
                PublishEvent p => $"PublishEvent:{p.Event.Name}",
                CreateEntityInstance c => $"CreateEntity:{c.EntityType.Name}",
                InvokeAction i => $"InvokeAction:{i.TargetAction.Name}",
                _ => effect.GetType().Name
            });
        }
        return string.Join(", ", parts);
    }

    private static void DrawBox(StringBuilder sb, IEnumerable<string> lines) {
        var horizontal = "+" + new string('-', BoxWidth - 2) + "+";
        sb.AppendLine(horizontal);
        foreach (var line in lines) {
            var clipped = line.Length > BoxWidth - 4 ? line[..(BoxWidth - 7)] + "..." : line;
            sb.AppendLine($"| {clipped.PadRight(BoxWidth - 4)} |");
        }
        sb.AppendLine(horizontal);
    }
}
