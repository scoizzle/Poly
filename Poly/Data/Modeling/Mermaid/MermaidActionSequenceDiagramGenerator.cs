using Poly.Data.Modeling.Effects;

namespace Poly.Data.Modeling.Mermaid;

/// <summary>
/// Generates Mermaid sequence diagrams for domain actions.
/// </summary>
public sealed class MermaidActionSequenceDiagramGenerator {
    private readonly StringBuilder _sb = new();

    /// <summary>
    /// Generates a Mermaid sequence diagram for one action.
    /// </summary>
    public string Generate(Action action) {
        ArgumentNullException.ThrowIfNull(action);

        _sb.Clear();
        _sb.AppendLine("sequenceDiagram");
        _sb.AppendLine("    actor Caller");
        _sb.AppendLine($"    participant Aggregate as {action.Entity.Name}");

        var participants = CollectEffectParticipants(action);
        foreach (var participant in participants) {
            _sb.AppendLine($"    participant {participant.Alias} as {participant.Name}");
        }

        var parameterList = string.Join(", ", action.Parameters.OfType<Property>().Select(p => p.Name));
        _sb.AppendLine($"    Caller->>Aggregate: {action.Name}({parameterList})");

        foreach (var effect in action.Effects) {
            switch (effect) {
                case StageTransition transition:
                    _sb.AppendLine($"    Aggregate->>Aggregate: transition to {transition.TargetStage.Name}");
                    break;
                case PublishEvent publish:
                    _sb.AppendLine($"    Aggregate-->>EventBus: publish {publish.Event.Name}");
                    break;
                case CreateEntityInstance create:
                    _sb.AppendLine($"    Aggregate->>Factory_{create.EntityType.Name}: create {create.EntityType.Name}");
                    if (create.InitialStage is not null) {
                        _sb.AppendLine($"    Factory_{create.EntityType.Name}->>Factory_{create.EntityType.Name}: set stage {create.InitialStage.Name}");
                    }
                    break;
                case InvokeAction invoke:
                    _sb.AppendLine($"    Aggregate->>{invoke.TargetAction.Entity.Name}: invoke {invoke.TargetAction.Name}");
                    break;
                default:
                    _sb.AppendLine($"    Aggregate->>Aggregate: apply {effect.GetType().Name}");
                    break;
            }
        }

        _sb.AppendLine("    Aggregate-->>Caller: completed");

        return _sb.ToString();
    }

    private static IReadOnlyCollection<(string Alias, string Name)> CollectEffectParticipants(Action action) {
        var participants = new Dictionary<string, (string Alias, string Name)>(StringComparer.Ordinal);

        foreach (var effect in action.Effects) {
            switch (effect) {
                case PublishEvent:
                    participants.TryAdd("EventBus", ("EventBus", "Event Bus"));
                    break;
                case CreateEntityInstance create:
                    var factoryAlias = $"Factory_{create.EntityType.Name}";
                    participants.TryAdd(factoryAlias, (factoryAlias, $"{create.EntityType.Name} Factory"));
                    break;
                case InvokeAction invoke:
                    var targetAlias = invoke.TargetAction.Entity.Name;
                    participants.TryAdd(targetAlias, (targetAlias, invoke.TargetAction.Entity.Name));
                    break;
            }
        }

        return participants.Values.ToArray();
    }
}