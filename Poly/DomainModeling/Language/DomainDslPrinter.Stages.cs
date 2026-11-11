using Poly.DomainModeling.Ontology;

using Not = Poly.DomainModeling.Ontology.Not;

namespace Poly.DomainModeling.Language;

// DomainDslPrinter partial: stage declaration and stage-require printing (G-00b mechanical split).
public sealed partial class DomainDslPrinter {
    private void PrintStage(Stage stage, string indent, Entity entity) {
        _sb.Append(indent);
        _sb.Append(stage.Name);
        _sb.Append(": stage");

        // A stage require is a use of an entity policy (same expression instance), or the
        // negation of one. Anything else cannot be read back; do not drop it. Printed in the
        // header, laid out as an action's requires.
        var positiveRequires = new List<string>();
        var negatedRequires = new List<string>();
        foreach (var policy in stage.Policies) {
            var (name, negated) = StageRequire(entity, policy)
                ?? throw new InvalidOperationException(
                    $"Stage '{stage.Name}' policy '{policy.Name}' is not a require of an entity policy, so it cannot be printed.");
            (negated ? negatedRequires : positiveRequires).Add(name);
        }
        if (positiveRequires.Count > 0) {
            _sb.AppendLine();
            _sb.Append(indent);
            _sb.Append("  require ");
            _sb.Append(string.Join(", ", positiveRequires));
        }
        foreach (var name in negatedRequires) {
            _sb.AppendLine();
            _sb.Append(indent);
            _sb.Append("  require not ");
            _sb.Append(name);
        }
        _sb.AppendLine(" {");

        // P2.4: Print OnEntry/OnExit effects
        if (stage.OnEntryEffects.Count > 0) {
            _sb.Append(indent);
            _sb.AppendLine("  entry {");
            foreach (var effect in stage.OnEntryEffects) {
                _sb.Append(indent);
                _sb.Append("    ");
                PrintEffect(effect, indent + "  ");
            }
            _sb.Append(indent);
            _sb.AppendLine("  }");
        }

        if (stage.OnExitEffects.Count > 0) {
            _sb.Append(indent);
            _sb.AppendLine("  exit {");
            foreach (var effect in stage.OnExitEffects) {
                _sb.Append(indent);
                _sb.Append("    ");
                PrintEffect(effect, indent + "  ");
            }
            _sb.Append(indent);
            _sb.AppendLine("  }");
        }

        // Subscriptions
        foreach (var sub in stage.Subscriptions) {
            PrintSubscription(sub, indent + "  ");
        }

        // Actions
        foreach (var action in stage.Actions) {
            PrintAction(action, indent + "  ", stageName: stage.Name);
        }

        _sb.Append(indent);
        _sb.AppendLine("}");
    }

    /// <summary>
    /// The entity policy name when the stage policy is that policy's expression instance,
    /// or the name with negated when it is <c>Not</c> of that instance. A <c>not_</c> prefix
    /// alone is not enough. Null when the policy cannot be printed as a require.
    /// </summary>
    private static (string Name, bool Negated)? StageRequire(Entity entity, Policy policy) {
        var same = entity.Policies.FirstOrDefault(p =>
            string.Equals(p.Name, policy.Name, StringComparison.Ordinal)
            && ReferenceEquals(p.Expression, policy.Expression));
        if (same is not null)
            return (same.Name, false);
        if (policy.Expression is Not not) {
            var negated = entity.Policies.FirstOrDefault(p =>
                string.Equals("not_" + p.Name, policy.Name, StringComparison.Ordinal)
                && ReferenceEquals(p.Expression, not.Operand));
            if (negated is not null)
                return (negated.Name, true);
        }
        return null;
    }
}