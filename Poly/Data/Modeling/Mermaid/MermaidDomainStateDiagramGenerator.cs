using Poly.Data.Modeling.Effects;

namespace Poly.Data.Modeling.Mermaid;

/// <summary>
/// Generates Mermaid state diagrams from domain types that define stages.
/// </summary>
public sealed class MermaidDomainStateDiagramGenerator {
    private readonly StringBuilder _sb = new();

    /// <summary>
    /// Generates a Mermaid stateDiagram-v2 for every domain type with stages.
    /// </summary>
    public string Generate(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);

        _sb.Clear();
        _sb.AppendLine("stateDiagram-v2");

        foreach (var type in domain.Types.OfType<Entity>().Where(t => t.Stages.Count > 0)) {
            EmitTypeStateMachine(type);
        }

        foreach (var relationship in domain.Relationships.Where(r => r.Stages.Count > 0)) {
            EmitTypeStateMachine(relationship);
        }

        return _sb.ToString();
    }

    private void EmitTypeStateMachine(Entity type) {
        var typeId = BuildNodeId(type.Name);
        _sb.AppendLine($"    state \"{type.Name}\" as {typeId} {{");

        var stageIds = type.Stages.ToDictionary(
            stage => stage,
            stage => BuildNodeId(type.Name, stage.Name));

        var firstStage = type.Stages.FirstOrDefault();
        if (firstStage is not null) {
            _sb.AppendLine($"        [*] --> {stageIds[firstStage]}");
        }

        foreach (var stage in type.Stages) {
            var stageId = stageIds[stage];
            _sb.AppendLine($"        state \"{stage.Name}\" as {stageId}");

            if (stage.Parent is not null && stageIds.TryGetValue(stage.Parent, out var parentId)) {
                _sb.AppendLine($"        {parentId} --> {stageId} : substage");
            }
        }

        foreach (var stage in type.Stages) {
            var fromId = stageIds[stage];
            foreach (var action in stage.Actions) {
                foreach (var transition in action.Effects.OfType<StageTransition>()) {
                    if (stageIds.TryGetValue(transition.TargetStage, out var toId)) {
                        _sb.AppendLine($"        {fromId} --> {toId} : {action.Name}");
                    }
                }
            }
        }

        _sb.AppendLine("    }");
    }

    private static string BuildNodeId(params string[] parts) {
        var raw = string.Join("_", parts);
        var chars = raw
            .Where(ch => char.IsLetterOrDigit(ch) || ch == '_')
            .ToArray();

        var id = new string(chars);
        if (string.IsNullOrWhiteSpace(id)) {
            return "State";
        }

        return char.IsDigit(id[0]) ? $"S_{id}" : id;
    }
}