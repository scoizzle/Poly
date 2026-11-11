using Poly.DomainModeling.Ontology;

using Action = Poly.DomainModeling.Ontology.Action;

namespace Poly.DomainModeling.Language;

// DomainDslPrinter partial: action declaration printing (G-00b mechanical split).
public sealed partial class DomainDslPrinter {
    private void PrintAction(Action action, string indent, string? stageName) {
        _sb.Append(indent);
        if (action.Visibility == Visibility.Public) {
            _sb.Append("public action ");
            _sb.Append(action.Name);
        }
        else {
            _sb.Append(action.Name);
            // Keep Name: kind consistency with properties/stages/policies.
            // Params decorate the action after the kind: Name: action (p: Type, ...)
            _sb.Append(": action");
        }
        if (action.Parameters.Count > 0) {
            _sb.Append(" (");
            var firstParam = true;
            foreach (var param in action.Parameters) {
                if (!firstParam) _sb.Append(", ");
                firstParam = false;
                _sb.Append(param.Name);
                _sb.Append(": ");
                _sb.Append(param.Type.TypeName);
            }
            _sb.Append(')');
        }

        // Print return type when non-void: -> RetType
        if (action.Result is { Members.Count: > 0 }) {
            _sb.Append(" -> ");
            _sb.Append(action.Result.Members[0].Type.TypeName);
        }

        // Print require gates: positive + negated (skip internal when_* policies)
        var positiveRequires = action.Policies
            .Where(p => !p.Name.StartsWith("when_", StringComparison.Ordinal)
                     && !p.Name.StartsWith("not_", StringComparison.Ordinal))
            .Select(p => p.Name)
            .ToList();

        var negatedRequires = action.Policies
            .Where(p => p.Name.StartsWith("not_", StringComparison.Ordinal))
            .Select(p => p.Name.Substring(4)) // "not_X" → "X"
            .ToList();

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

        // Effects
        foreach (var effect in action.Effects) {
            _sb.Append(indent);
            _sb.Append("  ");
            PrintEffect(effect, indent);
        }

        _sb.Append(indent);
        _sb.AppendLine("}");
    }
}