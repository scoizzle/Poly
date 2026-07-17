using System.Text;

using Poly.DomainModeling.Constraints;
using Poly.DomainModeling.Effects;

namespace Poly.DomainModeling.Parsing;

/// <summary>
/// Canonical printer for the Phase 1a Poly DSL.
/// Converts a committed <see cref="Domain"/> to stable, deterministic .poly text.
///
/// Output is idempotent: printing the same domain twice produces identical text.
/// No event/publish/subscribe output (these were removed from the product surface).
/// Relationships use the explicit record form (N2 interim).
/// </summary>
public sealed class DomainDslPrinter {
    private readonly StringBuilder _sb = new();

    /// <summary>
    /// Prints the domain to .poly text.
    /// </summary>
    public string Print(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);
        _sb.Clear();

        // Domain header
        _sb.AppendLine($"domain {domain.Name}");
        _sb.AppendLine();

        // Entities (sorted by name for stability)
        var entities = domain.Types.OfType<Entity>()
            .OrderBy(e => e.Name, StringComparer.Ordinal)
            .ToList();

        foreach (var entity in entities) {
            PrintEntity(entity);
            _sb.AppendLine();
        }

        // Standalone policies (not on an entity — likely none in current IR)
        // Currently all policies live on entities, so this is a no-op.

        return _sb.ToString().TrimEnd() + "\n";
    }

    private void PrintEntity(Entity entity) {
        _sb.AppendLine($"{entity.Name}: entity {{");

        // Properties
        foreach (var prop in entity.Properties) {
            _sb.Append("  ");
            _sb.Append(prop.Name);
            _sb.Append(": ");
            _sb.Append(prop.Type.TypeName);

            foreach (var c in prop.Constraints) {
                _sb.Append(' ');
                _sb.Append(PrintConstraint(c));
            }
            _sb.AppendLine();
        }

        // Entity-level policies
        foreach (var policy in entity.Policies) {
            _sb.Append("  ");
            PrintPolicy(policy, "  ");
        }

        // Stages (in declared order)
        foreach (var stage in entity.Stages) {
            PrintStage(stage, "  ");
        }

        // Entity-level actions
        foreach (var action in entity.Actions) {
            PrintAction(action, "  ", stageName: null);
        }

        _sb.AppendLine("}");
    }

    private void PrintStage(Stage stage, string indent) {
        _sb.Append(indent);
        _sb.Append(stage.Name);
        _sb.Append(": stage");

        if (stage.Parent is not null) {
            _sb.Append(" prev ");
            _sb.Append(stage.Parent.StageName);
        }

        _sb.AppendLine(" {");

        // OnEntry effects
        foreach (var effect in stage.OnEntryEffects) {
            _sb.Append(indent);
            _sb.Append("  ");
            PrintEffect(effect, indent + "  ");
        }

        // OnExit effects
        foreach (var effect in stage.OnExitEffects) {
            _sb.Append(indent);
            _sb.Append("  ");
            PrintEffect(effect, indent + "  ");
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

    private void PrintAction(Action action, string indent, string? stageName) {
        _sb.Append(indent);
        _sb.Append(action.Name);
        _sb.Append(": action");

        // Stage gates — only print if they differ from the declaring stage
        if (action.Policies.Count > 0) {
            var requireNames = action.Policies
                .Where(p => !p.Name.StartsWith("when_", StringComparison.Ordinal))
                .Select(p => p.Name)
                .ToList();
            var whenNames = action.Policies
                .Where(p => p.Name.StartsWith("when_", StringComparison.Ordinal))
                .Select(p => p.Name[5..]) // strip "when_" prefix
                .ToList();

            if (whenNames.Count > 0) {
                _sb.AppendLine();
                _sb.Append(indent);
                _sb.Append("  when ");
                _sb.Append(string.Join(", ", whenNames));
            }

            if (requireNames.Count > 0) {
                _sb.AppendLine();
                _sb.Append(indent);
                _sb.Append("  require ");
                _sb.Append(string.Join(", ", requireNames));
            }
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

    private void PrintSubscription(StageSubscription sub, string indent) {
        _sb.Append(indent);
        _sb.Append("when ");
        _sb.Append(sub.RelationshipName);
        _sb.Append(' ');
        _sb.Append(string.Join(", ", sub.StageNames));
        _sb.AppendLine(" {");

        foreach (var effect in sub.Effects) {
            _sb.Append(indent);
            _sb.Append("  ");
            PrintEffect(effect, indent + "  ");
        }

        _sb.Append(indent);
        _sb.AppendLine("}");
    }

    private void PrintPolicy(Policy policy, string indent) {
        _sb.Append(policy.Name);
        _sb.Append(": policy { ");
        _sb.Append(PrintExpression(policy.Expression));
        _sb.AppendLine(" }");
    }

    private void PrintEffect(Effect effect, string indent) {
        switch (effect) {
            case StageTransitionEffect ste:
                _sb.Append("transition to ");
                _sb.Append(ste.TargetStage.StageName);
                _sb.AppendLine();
                break;

            case AssignEffect ae:
                _sb.Append("assign ");
                _sb.Append(PrintExpression(ae.Target));
                _sb.Append(" to ");
                _sb.Append(PrintExpression(ae.Value));
                _sb.AppendLine();
                break;

            case CompositeEffect ce:
                foreach (var sub in ce.Effects) {
                    PrintEffect(sub, indent);
                }
                break;

            case ConditionalEffect ce:
                _sb.Append("if ");
                _sb.Append(PrintExpression(ce.Condition));
                _sb.AppendLine(" {");
                foreach (var sub in ce.ThenEffects) {
                    _sb.Append(indent);
                    _sb.Append("  ");
                    PrintEffect(sub, indent + "  ");
                }
                _sb.Append(indent);
                _sb.AppendLine("}");
                if (ce.ElseEffects is { Count: > 0 }) {
                    _sb.Append(indent);
                    _sb.AppendLine("else {");
                    foreach (var sub in ce.ElseEffects) {
                        _sb.Append(indent);
                        _sb.Append("  ");
                        PrintEffect(sub, indent + "  ");
                    }
                    _sb.Append(indent);
                    _sb.AppendLine("}");
                }
                break;

            default:
                _sb.AppendLine($"// Effect type '{effect.GetType().Name}' not printable in Phase 1a");
                break;
        }
    }

    private string PrintExpression(DomainExpression expr) {
        return expr switch {
            PropertyAccess p => p.Name,
            ParameterAccess p => p.Name,
            Literal l => PrintLiteral(l),
            OwnedAccess o => $"{o.OwnedName}.{PrintExpression(o.Inner)}",
            Comparison c => $"{PrintExpression(c.Left)} {PrintComparisonKind(c.Kind)} {PrintExpression(c.Right)}",
            And a => $"({PrintExpression(a.Left)} and {PrintExpression(a.Right)})",
            Or o => $"({PrintExpression(o.Left)} or {PrintExpression(o.Right)})",
            Not n => $"not {PrintExpression(n.Operand)}",
            Add a => $"({PrintExpression(a.Left)} + {PrintExpression(a.Right)})",
            Subtract s => $"({PrintExpression(s.Left)} - {PrintExpression(s.Right)})",
            Multiply m => $"({PrintExpression(m.Left)} * {PrintExpression(m.Right)})",
            Divide d => $"({PrintExpression(d.Left)} / {PrintExpression(d.Right)})",
            Exists e => $"exists({PrintExpression(e.Target)})",
            NotExists n => $"not_exists({PrintExpression(n.Target)})",
            _ => $"?{expr.GetType().Name}",
        };
    }

    private static string PrintLiteral(Literal literal) {
        if (literal.Value is null) return "null";
        if (literal.Value is bool b) return b ? "true" : "false";
        if (literal.Value is string s) return $"\"{s}\"";
        if (literal.Value is long l) return l.ToString();
        if (literal.Value is double d) return d.ToString("0.#");
        return literal.Value.ToString() ?? "null";
    }

    private static string PrintComparisonKind(ComparisonKind kind) => kind switch {
        ComparisonKind.Equal => "is",
        ComparisonKind.NotEqual => "is not",
        ComparisonKind.LessThan => "<",
        ComparisonKind.LessThanOrEqual => "<=",
        ComparisonKind.GreaterThan => ">",
        ComparisonKind.GreaterThanOrEqual => ">=",
        _ => "?",
    };

    private static string PrintConstraint(Constraint constraint) => constraint switch {
        RequiredConstraint => "required",
        UniqueConstraint => "unique",
        RangeConstraint r => $"range({r.Minimum?.ToString() ?? ""}, {r.Maximum?.ToString() ?? ""})",
        LengthConstraint l => l.MinLength == l.MaxLength
            ? $"length({l.MinLength})"
            : $"length({l.MinLength}, {l.MaxLength})",
        PatternConstraint p => $"pattern(\"{p.Pattern}\")",
        _ => $"?{constraint.GetType().Name}",
    };
}