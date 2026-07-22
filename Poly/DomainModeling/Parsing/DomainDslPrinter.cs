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
    private readonly AnnotationRegistry? _annotations;

    /// <summary>Creates a printer with an optional annotation registry for facet printing.</summary>
    public DomainDslPrinter(AnnotationRegistry? annotations = null) {
        _annotations = annotations;
    }

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

        // Print enum types first
        var enumTypes = domain.Types.OfType<EnumType>().ToList();
        foreach (var enumType in enumTypes) {
            PrintEnumType(enumType);
            _sb.AppendLine();
        }

        foreach (var entity in entities) {
            PrintEntity(entity);
            _sb.AppendLine();
        }

        return _sb.ToString().TrimEnd() + "\n";
    }

    private void PrintEnumType(EnumType enumType) {
        _sb.AppendLine($"{enumType.Name}: enum {{");
        foreach (var member in enumType.MemberNames) {
            _sb.AppendLine($"  {member},");
        }
        _sb.AppendLine("}");
        _sb.AppendLine();
    }

    private void PrintEntity(Entity entity) {
        _sb.Append(entity.Name);
        _sb.Append(": entity");

        foreach (var facet in entity.Facets) {
            _sb.Append(' ');
            _sb.Append(PrintFacet(facet));
        }

        _sb.AppendLine(" {");

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

            foreach (var facet in prop.Facets) {
                _sb.Append(' ');
                _sb.Append(PrintFacet(facet, prop.Name));
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

        // Entity-level subscriptions
        foreach (var sub in entity.Subscriptions) {
            PrintSubscription(sub, "  ");
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

    private string PrintFacet(Facet facet, string? propertyName = null) {
        var text = _annotations?.TryPrint(facet);
        if (text is not null)
            return text;

        var location = propertyName is null ? "" : $" on property '{propertyName}'";
        if (facet is Annotation ann) {
            throw new FormatException(
                $"Cannot print annotation '{ann.Name}'{location} — no pack registered for this keyword.");
        }

        throw new FormatException(
            $"Cannot print facet of type '{facet.GetType().Name}'{location} — no pack registered for it.");
    }

    private void PrintEffect(Effect effect, string indent) {
        EffectPrinter.Run(_sb, this, effect, indent);
    }

    /// <summary>
    /// Effect dispatch for printing. Methods are named by the Effect subtype.
    /// The verb (print) comes from the containing DomainDslPrinter.
    /// </summary>
    private sealed class EffectPrinter : EffectDispatch<object?> {
        private readonly StringBuilder _sb;
        private readonly DomainDslPrinter _printer;
        private string _indent = "";

        private EffectPrinter(StringBuilder sb, DomainDslPrinter printer) {
            _sb = sb;
            _printer = printer;
        }

        protected override object? Default() => null;

        public static void Run(StringBuilder sb, DomainDslPrinter printer, Effect effect, string indent) {
            var ep = new EffectPrinter(sb, printer) { _indent = indent };
            ep.Route(effect);
        }

        protected override object? StageTransition(StageTransitionEffect ste) {
            _sb.Append("transition to ");
            _sb.Append(ste.TargetStage.StageName);
            _sb.AppendLine();
            return null;
        }

        protected override object? Assign(AssignEffect ae) {
            _sb.Append("assign ");
            _sb.Append(_printer.PrintExpression(ae.Target));
            _sb.Append(" to ");
            _sb.Append(_printer.PrintExpression(ae.Value));
            _sb.AppendLine();
            return null;
        }

        protected override object? Composite(CompositeEffect ce) {
            foreach (var sub in ce.Effects) {
                Run(_sb, _printer, sub, _indent);
            }
            return null;
        }

        protected override object? Conditional(ConditionalEffect ce) {
            _printer.PrintConditionalEffect(ce, _indent);
            return null;
        }

        protected override object? CreateEntityInstance(CreateEntityInstance create) {
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
                _sb.Append(_printer.PrintExpression(init.Expression));
                first = false;
            }
            if (!first) _sb.Append(' ');
            _sb.AppendLine("}");
            return null;
        }

        protected override object? CreateEntityInRelationship(CreateEntityInRelationshipEffect createIn) {
            _sb.Append("create in ");
            _sb.Append(createIn.RelationshipName);
            _sb.Append(" {");
            var firstInit = true;
            foreach (var init in createIn.Initializers) {
                if (!firstInit) _sb.Append(',');
                _sb.Append(' ');
                _sb.Append(init.PropertyName);
                _sb.Append(": ");
                _sb.Append(_printer.PrintExpression(init.Expression));
                firstInit = false;
            }
            if (!firstInit) _sb.Append(' ');
            _sb.AppendLine("}");
            return null;
        }

        protected override object? InvokeAction(InvokeActionEffect invoke) {
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
                    _sb.Append(binding.PropertyName);
                    _sb.Append(": ");
                    _sb.Append(_printer.PrintExpression(binding.Expression));
                    firstBinding = false;
                }
                _sb.Append(')');
            }
            if (invoke.Quantifier is not null && invoke.TargetRelationship is not null)
                _printer.PrintFilter(invoke);
            _sb.AppendLine();
            return null;
        }

        protected override object? DeleteEntity(DeleteEntityInstance _) {
            _sb.AppendLine("delete");
            return null;
        }
    }

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
            if (ce.ElseEffects is [ConditionalEffect nestedOnly]) {
                _sb.AppendLine();
                _sb.Append(indent);
                _sb.Append("else ");
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

    private void PrintFilter(InvokeActionEffect invoke) {
        _sb.Append(" where ");
        _sb.Append(PrintExpression(invoke.Filter!));
    }

    private string PrintExpression(DomainExpression expr) {
        return ExpressionPrinter.Run(this, expr);
    }

    /// <summary>
    /// Expression dispatch for printing. Methods are named by the Expression subtype.
    /// The verb (print) comes from the containing DomainDslPrinter.
    /// </summary>
    private sealed class ExpressionPrinter : DomainExpressionDispatch<string> {
        private readonly DomainDslPrinter _printer;

        private ExpressionPrinter(DomainDslPrinter printer) => _printer = printer;

        protected override string Default() => $"?{CurrentExpr?.GetType().Name}";
        private DomainExpression? CurrentExpr { get; set; }

        public static string Run(DomainDslPrinter printer, DomainExpression expr) {
            var ep = new ExpressionPrinter(printer) { CurrentExpr = expr };
            return ep.Route(expr);
        }

        protected override string PropertyAccess(Poly.DomainModeling.PropertyAccess p) => p.Name;
        protected override string ParameterAccess(Poly.DomainModeling.ParameterAccess p) => p.Name;
        protected override string Literal(Poly.DomainModeling.Literal l) => DomainDslPrinter.PrintLiteral(l);

        protected override string OwnedAccess(Poly.DomainModeling.OwnedAccess o) =>
            $"{o.OwnedName} {Run(_printer, o.Inner)}";

        protected override string RelationshipNavigation(Poly.DomainModeling.RelationshipNavigation r) =>
            _printer.PrintRelationshipNav(r);

        protected override string Comparison(Poly.DomainModeling.Comparison c) =>
            $"{Run(_printer, c.Left)} {DomainDslPrinter.PrintComparisonKind(c.Kind)} {Run(_printer, c.Right)}";

        protected override string And(Poly.DomainModeling.And a) =>
            $"({Run(_printer, a.Left)} and {Run(_printer, a.Right)})";

        protected override string Or(Poly.DomainModeling.Or o) =>
            $"({Run(_printer, o.Left)} or {Run(_printer, o.Right)})";

        protected override string Not(Poly.DomainModeling.Not n) =>
            $"not {Run(_printer, n.Operand)}";

        protected override string Add(Poly.DomainModeling.Add a) =>
            $"({Run(_printer, a.Left)} + {Run(_printer, a.Right)})";

        protected override string Subtract(Poly.DomainModeling.Subtract s) =>
            $"({Run(_printer, s.Left)} - {Run(_printer, s.Right)})";

        protected override string Multiply(Poly.DomainModeling.Multiply m) =>
            $"({Run(_printer, m.Left)} * {Run(_printer, m.Right)})";

        protected override string Divide(Poly.DomainModeling.Divide d) =>
            $"({Run(_printer, d.Left)} / {Run(_printer, d.Right)})";

        protected override string Exists(Poly.DomainModeling.Exists e) =>
            $"{Run(_printer, e.Target)} exists";

        protected override string NotExists(Poly.DomainModeling.NotExists n) =>
            $"not {Run(_printer, n.Target)} exists";

        protected override string AnyExpr(Poly.DomainModeling.AnyExpr a) =>
            $"any {a.RelationshipName} where {Run(_printer, a.Body)}";

        protected override string AllExpr(Poly.DomainModeling.AllExpr a) =>
            $"all {a.RelationshipName} where {Run(_printer, a.Body)}";

        protected override string NoneExpr(Poly.DomainModeling.NoneExpr n) =>
            $"none {n.RelationshipName} where {Run(_printer, n.Body)}";

        protected override string CountExpr(Poly.DomainModeling.CountExpr c) =>
            c.Body is not null
                ? $"count {c.RelationshipName} where {Run(_printer, c.Body)}"
                : $"count {c.RelationshipName}";
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
    public static string EscapeStringLiteral(string value) =>
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
        DefaultValueConstraint dv => $"default({PrintDomainExpression(dv.Expression)})",
        EqualityConstraint e => $"/* equals({PrintLiteralValue(e.ExpectedValue)}) */", // legacy
        EnumConstraint en => $"/* enum({string.Join(", ", en.Members.Select(m => m.Name))}) */", // legacy — no longer parsed
        _ => $"?{constraint.GetType().Name}",
    };

    private static string PrintDomainExpression(DomainExpression expr) => expr switch {
        Poly.DomainModeling.Literal l => PrintLiteralValue(l.Value),
        Poly.DomainModeling.PropertyAccess pa => pa.Name,
        _ => expr.ToString() ?? "?",
    };

    private static string PrintLiteralValue(object? value) => value switch {
        null => "null",
        true => "true",
        false => "false",
        string s => $"\"{EscapeStringLiteral(s)}\"",
        _ => value.ToString() ?? "null",
    };
}