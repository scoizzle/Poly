using System.Text;

using Poly.DomainModeling;
using Poly.DomainModeling.Constraints;
using Poly.DomainModeling.Effects;

namespace Poly.DomainModeling.Parsing;

/// <summary>
/// Canonical printer for the Phase 1a Poly DSL.
/// Converts a committed <see cref="Domain"/> to stable, deterministic .poly text.
///
/// Output is idempotent: printing the same domain twice produces identical text.
/// No event/publish/subscribe output (these were removed from the product surface).
/// Relationships are printed as inline navigation properties on the source entity
/// (N1 form only): "orders: many Order". The legacy top-level
/// "relationship Name from ... to ..." form is not printed and is not accepted by the parser.
/// </summary>
public sealed class DomainDslPrinter {
    private readonly StringBuilder _sb = new();
    private IReadOnlyList<Relationship> _relationships = [];

    /// <summary>
    /// Prints the domain to .poly text.
    /// </summary>
    public string Print(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);
        _sb.Clear();
        _relationships = domain.Relationships;

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

        return _sb.ToString().TrimEnd() + "\n";
    }

    private void PrintEntity(Entity entity) {
        var header = entity.ParentEntityName is not null
            ? $"{entity.Name}: {entity.ParentEntityName} entity {{"
            : $"{entity.Name}: entity {{";
        _sb.AppendLine(header);

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

        // Navigation properties (N1 source-side only)
        foreach (var rel in _relationships
            .Where(r => string.Equals(r.Source.TypeName, entity.Name, StringComparison.Ordinal))
            .OrderBy(r => r.Name, StringComparer.Ordinal)) {
            _sb.Append("  ");
            _sb.Append(rel.Name);
            _sb.Append(": ");
            var isMany = rel.Cardinality is RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany;
            if (isMany) {
                _sb.Append("many ");
            }
            if (rel.SourceOwnsTarget) {
                _sb.Append("owned ");
            }
            _sb.Append(rel.Target.TypeName);
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
        _sb.Append(": stage {");
        _sb.AppendLine();

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

    private void PrintAction(Action action, string indent, string? stageName) {
        _sb.Append(indent);
        _sb.Append(action.Name);
        // Keep Name: kind consistency with properties/stages/policies.
        // Params decorate the action after the kind: Name: action (p: Type, ...)
        _sb.Append(": action");
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

        if (positiveRequires.Count > 0 || negatedRequires.Count > 0) {
            _sb.AppendLine();
            _sb.Append(indent);
            _sb.Append("  require ");
            var parts = new List<string>();
            parts.AddRange(positiveRequires);
            parts.AddRange(negatedRequires.Select(n => $"not {n}"));
            _sb.Append(string.Join(", ", parts));
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
                PrintConditionalEffect(ce, indent);
                break;

            case CreateEntityInstance create:
                // P2′.5: If RelationshipName is set, print as "create in RelName { ... }" instead
                if (create.RelationshipName is not null) {
                    _sb.Append("create in ");
                    _sb.Append(create.RelationshipName);
                    _sb.Append(" {");
                }
                else {
                    _sb.Append("create ");
                    _sb.Append(create.Type.TypeName);
                    _sb.Append(" {");
                }
                var first = true;
                foreach (var init in create.Initializers) {
                    if (!first) _sb.Append(',');
                    _sb.Append(' ');
                    _sb.Append(init.PropertyName);
                    _sb.Append(": ");
                    _sb.Append(PrintExpression(init.Expression));
                    first = false;
                }
                if (!first) _sb.Append(' ');
                _sb.AppendLine("}");
                break;

            case CreateEntityInRelationshipEffect createIn:
                _sb.Append("create in ");
                _sb.Append(createIn.RelationshipName);
                _sb.Append(" {");
                var firstInit = true;
                foreach (var init in createIn.Initializers) {
                    if (!firstInit) _sb.Append(',');
                    _sb.Append(' ');
                    _sb.Append(init.PropertyName);
                    _sb.Append(": ");
                    _sb.Append(PrintExpression(init.Expression));
                    firstInit = false;
                }
                if (!firstInit) _sb.Append(' ');
                _sb.AppendLine("}");
                break;

            case InvokeActionEffect invoke:
                _sb.Append("invoke ");
                if (invoke.Quantifier == StageSubscriptionQuantifier.Any)
                    _sb.Append("any ");
                else if (invoke.Quantifier == StageSubscriptionQuantifier.All)
                    _sb.Append("all ");
                if (invoke.TargetRelationship is not null) {
                    _sb.Append(invoke.TargetRelationship);
                    _sb.Append('.');
                }
                _sb.Append(invoke.ActionName);
                if (invoke.ParameterBindings.Count > 0) {
                    _sb.Append('(');
                    var firstBinding = true;
                    foreach (var binding in invoke.ParameterBindings) {
                        if (!firstBinding) _sb.Append(", ");
                        firstBinding = false;
                        _sb.Append(binding.PropertyName);
                        _sb.Append(": ");
                        _sb.Append(PrintExpression(binding.Expression));
                    }
                    _sb.Append(')');
                }
                if (invoke.Filter is not null) {
                    _sb.Append(" where ");
                    _sb.Append(PrintExpression(invoke.Filter));
                }
                _sb.AppendLine();
                break;

            case DeleteEntityInstance:
                _sb.AppendLine("delete");
                break;

            default:
                _sb.AppendLine($"// Effect type '{effect.GetType().Name}' not printable in Phase 1a");
                break;
        }
    }

    /// <summary>
    /// Prints <c>if / else if / else</c>. A single nested <see cref="ConditionalEffect"/>
    /// in the else branch is emitted as <c>else if</c> (E6.4 sugar).
    /// </summary>
    private void PrintConditionalEffect(ConditionalEffect ce, string indent) {
        _sb.AppendLine("if (" + PrintExpression(ce.Condition) + ") {");
        foreach (var sub in ce.ThenEffects) {
            _sb.Append(indent);
            _sb.Append("    ");
            PrintEffect(sub, indent + "  ");
        }
        _sb.Append(indent);
        _sb.Append('}');

        if (ce.ElseEffects is { Count: > 0 }) {
            // Collapse else { if (...) } → else if (...)
            if (ce.ElseEffects is [ConditionalEffect nestedOnly]) {
                _sb.AppendLine();
                _sb.Append(indent);
                _sb.Append("else ");
                // Continue on same indent — PrintConditionalEffect writes "if (...)"
                PrintConditionalEffect(nestedOnly, indent);
                return;
            }

            _sb.AppendLine();
            _sb.Append(indent);
            _sb.AppendLine("else {");
            foreach (var sub in ce.ElseEffects) {
                _sb.Append(indent);
                _sb.Append("    ");
                PrintEffect(sub, indent + "  ");
            }
            _sb.Append(indent);
            _sb.Append('}');
        }

        _sb.AppendLine();
    }

    private string PrintExpression(DomainExpression expr) {
        return expr switch {
            PropertyAccess p => p.Name,
            ParameterAccess p => p.Name,
            Literal l => PrintLiteral(l),
            OwnedAccess o => $"{o.OwnedName} {PrintExpression(o.Inner)}",
            RelationshipNavigation r => PrintRelationshipNav(r),
            Comparison c => $"{PrintExpression(c.Left)} {PrintComparisonKind(c.Kind)} {PrintExpression(c.Right)}",
            And a => $"({PrintExpression(a.Left)} and {PrintExpression(a.Right)})",
            Or o => $"({PrintExpression(o.Left)} or {PrintExpression(o.Right)})",
            Not n => $"not {PrintExpression(n.Operand)}",
            Add a => $"({PrintExpression(a.Left)} + {PrintExpression(a.Right)})",
            Subtract s => $"({PrintExpression(s.Left)} - {PrintExpression(s.Right)})",
            Multiply m => $"({PrintExpression(m.Left)} * {PrintExpression(m.Right)})",
            Divide d => $"({PrintExpression(d.Left)} / {PrintExpression(d.Right)})",
            Exists e => $"{PrintExpression(e.Target)} exists",
            NotExists n => $"not {PrintExpression(n.Target)} exists",
            AnyExpr a => $"any {a.RelationshipName} where {PrintExpression(a.Body)}",
            AllExpr a => $"all {a.RelationshipName} where {PrintExpression(a.Body)}",
            NoneExpr n => $"none {n.RelationshipName} where {PrintExpression(n.Body)}",
            CountExpr c when c.Body is not null => $"count {c.RelationshipName} where {PrintExpression(c.Body)}",
            CountExpr c => $"count {c.RelationshipName}",
            _ => $"?{expr.GetType().Name}",
        };
    }

    /// <summary>
    /// Prints a RelationshipNavigation in subject-first form.
    /// Simple body (PropertyAccess or Comparison) → "Rel Prop" / "Rel Prop op value"
    /// Complex body (And/Or etc) → "Rel where body" 
    /// </summary>
    private string PrintRelationshipNav(RelationshipNavigation rn) {
        var rel = rn.RelationshipName;
        var body = rn.TargetProperty;

        // Simple: Rel Prop
        if (body is PropertyAccess pa)
            return $"{rel} {pa.Name}";

        // Simple: Rel Prop op value (Comparison with PropertyAccess on left)
        if (body is Comparison comp && comp.Left is PropertyAccess)
            return $"{rel} {PrintExpression(body)}";

        // Complex: Rel where body
        return $"{rel} where {PrintExpression(body)}";
    }

    private static string PrintLiteral(Literal literal) {
        if (literal.Value is null) return "null";
        if (literal.Value is bool b) return b ? "true" : "false";
        if (literal.Value is string s) return $"\"{EscapeStringLiteral(s)}\"";
        if (literal.Value is long l) return l.ToString();
        if (literal.Value is double d) return d.ToString("0.#");
        return literal.Value.ToString() ?? "null";
    }

    /// <summary>Escapes <c>\</c> and <c>"</c> for double-quoted DSL string literals.</summary>
    internal static string EscapeStringLiteral(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("\"", "\\\"", StringComparison.Ordinal);

    /// <summary>
    /// Prints a single expression tree (not wrapped in a full domain).
    /// Public entry point for test use.
    /// </summary>
    public string PrintTestExpression(DomainExpression expr) {
        return PrintExpression(expr);
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
        PatternConstraint p => $"pattern(\"{EscapeStringLiteral(p.Pattern)}\")",
        EqualityConstraint e => $"equals({PrintLiteralValue(e.ExpectedValue)})",
        EnumConstraint en => $"enum({string.Join(", ", en.Members.Select(m => m.Name))})",
        _ => $"?{constraint.GetType().Name}",
    };

    private static string PrintLiteralValue(object? value) => value switch {
        null => "null",
        true => "true",
        false => "false",
        string s => $"\"{EscapeStringLiteral(s)}\"",
        _ => value.ToString() ?? "null",
    };
}