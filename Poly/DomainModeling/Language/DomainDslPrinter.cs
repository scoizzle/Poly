using Poly.DomainModeling.Dispatch;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;
using Poly.Grammar;

using Action = Poly.DomainModeling.Ontology.Action;
using Add = Poly.DomainModeling.Ontology.Add;
using And = Poly.DomainModeling.Ontology.And;
using Divide = Poly.DomainModeling.Ontology.Divide;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Not = Poly.DomainModeling.Ontology.Not;
using Or = Poly.DomainModeling.Ontology.Or;
using Subtract = Poly.DomainModeling.Ontology.Subtract;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.DomainModeling.Language;

/// <summary>
/// Canonical printer for the product Poly DSL (domain-graph walk).
/// GI-6: expression print resolves binders → Grammar <c>Printer</c> →
/// <see cref="DslTokenWriter"/>; this type remains the product print façade.
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
    private readonly AnnotationRegistry? _annotations;
    private readonly ExpressionPrintRegistry _expressionBinders = new();
    private readonly Printer<DslToken, DslTokenKind> _printTable;

    /// <summary>Creates a printer with the default (empty) session.</summary>
    public DomainDslPrinter() : this(DomainSession.ForExtensions([])) {
    }

    /// <summary>Creates a printer with an annotation registry for facet printing.</summary>
    public DomainDslPrinter(AnnotationRegistry annotations) {
        ArgumentNullException.ThrowIfNull(annotations);
        var language = DslGrammar.LanguageFor(annotations, new ExpressionFormRegistry());
        _annotations = annotations;
        new ExpressionFormRegistry().ContributePrintMappings(_expressionBinders);
        CoreExpressionPrintBinders.Register(_expressionBinders);
        _printTable = language.Printer;
    }

    public DomainDslPrinter(DomainSession session) {
        ArgumentNullException.ThrowIfNull(session);
        _annotations = session.Annotations;
        session.ExpressionForms.ContributePrintMappings(_expressionBinders);
        CoreExpressionPrintBinders.Register(_expressionBinders);
        _printTable = session.Language.Printer;
    }

    /// <summary>
    /// Prints the domain to .poly text.
    /// </summary>
    public string Print(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);
        _sb.Clear();

        _sb.AppendLine(_printTable.Print(
            "document",
            "header",
            fills: new Dictionary<string, string>(StringComparer.Ordinal) { ["name"] = domain.Name }));
        foreach (var extensionId in domain.Extensions) {
            _sb.AppendLine(_printTable.Print(
                "uses",
                "id",
                fills: new Dictionary<string, string>(StringComparer.Ordinal) { ["id"] = extensionId }));
        }
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

        foreach (var valueType in domain.Types.OfType<ValueType>().OrderBy(v => v.Name, StringComparer.Ordinal)) {
            PrintValueType(valueType);
            _sb.AppendLine();
        }

        foreach (var entity in entities) {
            PrintEntity(entity);
            _sb.AppendLine();
        }

        foreach (var contract in domain.ImportedContracts.OrderBy(c => c.Name, StringComparer.Ordinal)) {
            PrintContract(contract);
            _sb.AppendLine();
        }

        foreach (var binding in domain.ContractBindings.OrderBy(b => b.Name, StringComparer.Ordinal)) {
            PrintBinding(binding);
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

    private void PrintValueType(ValueType valueType) {
        _sb.AppendLine($"{valueType.Name}: value {{");
        foreach (var prop in valueType.Properties) {
            _sb.Append("  ");
            _sb.Append(prop.Name);
            _sb.Append(": ");
            _sb.Append(prop.Type.TypeName);
            foreach (var c in prop.Constraints) {
                var text = PrintConstraint(c);
                if (text.Length == 0)
                    continue;
                _sb.Append(' ');
                _sb.Append(text);
            }
            _sb.AppendLine();
        }
        _sb.AppendLine("}");
    }

    private void PrintContract(ImportedContract contract) {
        var kind = contract.SourceKind == ContractSourceKind.ExternalProvider ? "external" : "internal";
        _sb.Append(contract.Name);
        _sb.Append(": contract ");
        _sb.Append(kind);
        _sb.Append(' ');
        _sb.Append(NeedsQuotes(contract.SourceIdentifier) ? $"\"{EscapeStringLiteral(contract.SourceIdentifier)}\"" : contract.SourceIdentifier);
        _sb.Append(' ');
        _sb.Append(NeedsQuotes(contract.Version) ? $"\"{EscapeStringLiteral(contract.Version)}\"" : contract.Version);
        _sb.AppendLine(" {");
        foreach (var vt in contract.Types) {
            _sb.Append("  ");
            _sb.Append(vt.Name);
            _sb.AppendLine(": value {");
            foreach (var prop in vt.Properties) {
                _sb.Append("    ");
                _sb.Append(prop.Name);
                _sb.Append(": ");
                _sb.Append(prop.Type.TypeName);
                foreach (var c in prop.Constraints) {
                    var text = PrintConstraint(c);
                    if (text.Length == 0)
                        continue;
                    _sb.Append(' ');
                    _sb.Append(text);
                }
                _sb.AppendLine();
            }
            _sb.AppendLine("  }");
        }
        foreach (var ep in contract.Endpoints) {
            _sb.Append("  ");
            _sb.Append(ep.Name);
            _sb.Append(": ");
            _sb.Append(ep.Direction == ContractEndpointDirection.Inbound ? "inbound" : "outbound");
            _sb.Append(' ');
            _sb.Append(ep.Kind == ContractEndpointKind.Operation ? "operation" : "event");
            _sb.Append(' ');
            _sb.Append(ep.PayloadType.TypeName);
            _sb.AppendLine();
        }
        _sb.AppendLine("}");
    }

    private void PrintBinding(ContractBinding binding) {
        _sb.Append(binding.Name);
        _sb.Append(": bind ");
        _sb.Append(binding.ContractName);
        _sb.Append(' ');
        _sb.Append(binding.EndpointName);
        _sb.Append(" to ");
        _sb.Append(binding.ActionName);
        _sb.Append(' ');
        _sb.Append(binding.LocalParameterName);
        _sb.AppendLine();
    }

    private static bool NeedsQuotes(string value) =>
        value.Length == 0 || value.Any(c => !char.IsLetterOrDigit(c) && c != '_');

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
                var text = PrintConstraint(c);
                if (text.Length == 0)
                    continue;
                _sb.Append(' ');
                _sb.Append(text);
            }

            foreach (var facet in prop.Facets) {
                _sb.Append(' ');
                _sb.Append(PrintFacet(facet, prop.Name));
            }

            _sb.AppendLine();
        }

        // Navigation properties (N1 source-side only)
        foreach (var rel in entity.Navigations
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

    private void PrintSubscription(StageSubscription sub, string indent) {
        _sb.Append(indent);
        _sb.Append("when ");
        // P4-1: emit quantifier keyword only when not the default (Each).
        if (sub.Quantifier != StageSubscriptionQuantifier.Each) {
            _sb.Append(sub.Quantifier.ToString().ToLowerInvariant());
            _sb.Append(' ');
        }
        _sb.Append(sub.RelationshipName);
        _sb.Append(' ');
        _sb.Append(string.Join(", ", sub.StageNames));
        if (!string.IsNullOrEmpty(sub.PeerBinding)) {
            _sb.Append(" as ");
            _sb.Append(sub.PeerBinding);
        }
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
            _sb.AppendLine();
            return null;
        }

        protected override object? ForEachInvoke(ForEachInvokeEffect efe) {
            _sb.Append($"for {efe.RelationshipName} as {efe.BinderName}");
            switch (efe.Predicate) {
                case ForEachNamedPolicy { PolicyName: var policyName }:
                    _sb.Append($" where {efe.BinderName} {policyName}");
                    break;
                case ForEachStageMembership { StageName: var stageName }:
                    _sb.Append($" where {efe.BinderName} in {stageName}");
                    break;
            }
            _sb.Append($" invoke {efe.BinderName}.{efe.ActionName}");
            if (efe.ParameterBindings.Count > 0) {
                _sb.Append('(');
                var firstBinding = true;
                foreach (var binding in efe.ParameterBindings) {
                    if (!firstBinding) _sb.Append(", ");
                    _sb.Append(binding.PropertyName);
                    _sb.Append(": ");
                    _sb.Append(_printer.PrintExpression(binding.Expression));
                    firstBinding = false;
                }
                _sb.Append(')');
            }
            _sb.AppendLine();
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

    private string PrintExpression(DomainExpression expr) {
        return ExpressionPrinter.Run(this, expr);
    }

    /// <summary>
    /// Table print for a bound expression: Grammar <see cref="Printer{TToken,TTokenKind}"/>
    /// → <see cref="DslTokenWriter"/>. A binder-supplied fill carries the pack spelling
    /// (e.g. <c>MAGIC</c>, <c>12 days</c>); otherwise the built-in per-type fill supplies
    /// identifier/literal text. Returns null when no binder matches (dispatch fallback).
    /// </summary>
    private string? TryPrintBoundExpression(DomainExpression expr) {
        if (!_expressionBinders.TryMap(expr, out var binding))
            return null;
        return _printTable.Print(
            binding.Rule,
            binding.Pattern,
            binding.Fill ?? (binding.NamedFills is null ? ctx => FillPrimaryText(ctx, expr) : null),
            binding.NamedFills);
    }

    private void FillPrimaryText(PrintContext<DslToken, DslTokenKind> ctx, DomainExpression expr) {
        switch (expr) {
            case PropertyAccess p:
                ctx.Emit(p.Name);
                return;
            case ParameterAccess p:
                ctx.Emit(p.Name);
                return;
            case Literal l:
                ctx.Emit(PrintLiteral(l));
                return;
            default:
                throw new InvalidOperationException(
                    $"Expression type '{expr.GetType().Name}' has no table print fill.");
        }
    }

    /// <summary>
    /// Expression dispatch for printing. Methods are named by the Expression subtype.
    /// The verb (print) comes from the containing DomainDslPrinter.
    /// </summary>
    private sealed class ExpressionPrinter : DomainExpressionDispatch<string> {
        private readonly DomainDslPrinter _printer;

        private ExpressionPrinter(DomainDslPrinter printer) => _printer = printer;

        protected override string Default() =>
            throw new InvalidOperationException(
                $"Cannot print expression type '{CurrentExpr?.GetType().Name}': no registered print binder or pattern.");

        private DomainExpression? CurrentExpr { get; set; }

        public static string Run(DomainDslPrinter printer, DomainExpression expr) {
            var ep = new ExpressionPrinter(printer) { CurrentExpr = expr };
            return ep.Print(expr);
        }

        private string Print(DomainExpression expr) {
            CurrentExpr = expr;
            return _printer.TryPrintBoundExpression(expr) ?? Route(expr);
        }

        protected override string PropertyAccess(PropertyAccess p) => p.Name;
        protected override string ParameterAccess(ParameterAccess p) => p.Name;
        protected override string Literal(Literal l) => DomainDslPrinter.PrintLiteral(l);

        protected override string OwnedAccess(OwnedAccess o) =>
            $"{o.OwnedName} {Run(_printer, o.Inner)}";

        protected override string RelationshipNavigation(RelationshipNavigation r) =>
            _printer.PrintRelationshipNav(r);

        protected override string Comparison(Comparison c) =>
            $"{Run(_printer, c.Left)} {DomainDslPrinter.PrintComparisonKind(c.Kind)} {Run(_printer, c.Right)}";

        protected override string And(And a) =>
            $"({Run(_printer, a.Left)} and {Run(_printer, a.Right)})";

        protected override string Or(Or o) =>
            $"({Run(_printer, o.Left)} or {Run(_printer, o.Right)})";

        protected override string Not(Not n) =>
            n.Operand is Poly.DomainModeling.Ontology.Comparison
                or Poly.DomainModeling.Ontology.And
                or Poly.DomainModeling.Ontology.Or
                or Poly.DomainModeling.Ontology.Add
                or Poly.DomainModeling.Ontology.Subtract
                or Poly.DomainModeling.Ontology.Multiply
                or Poly.DomainModeling.Ontology.Divide
                or Poly.DomainModeling.Ontology.Not
                ? $"not ({Run(_printer, n.Operand)})"
                : $"not {Run(_printer, n.Operand)}";

        protected override string Add(Add a) =>
            $"({Run(_printer, a.Left)} + {Run(_printer, a.Right)})";

        protected override string Subtract(Subtract s) =>
            $"({Run(_printer, s.Left)} - {Run(_printer, s.Right)})";

        protected override string Multiply(Multiply m) =>
            $"({Run(_printer, m.Left)} * {Run(_printer, m.Right)})";

        protected override string Divide(Divide d) =>
            $"({Run(_printer, d.Left)} / {Run(_printer, d.Right)})";

        protected override string Exists(Exists e) =>
            $"{Run(_printer, e.Target)} exists";

        protected override string NotExists(NotExists n) =>
            $"not {Run(_printer, n.Target)} exists";

        protected override string AnyExpr(AnyExpr a) =>
            $"any {a.RelationshipName} where {Run(_printer, a.Body)}";

        protected override string AllExpr(AllExpr a) =>
            $"all {a.RelationshipName} where {Run(_printer, a.Body)}";

        protected override string NoneExpr(NoneExpr n) =>
            $"none {n.RelationshipName} where {Run(_printer, n.Body)}";

        protected override string CountExpr(CountExpr c) =>
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
        EqualityConstraint => "",
        _ => $"?{constraint.GetType().Name}",
    };

    private static string PrintDomainExpression(DomainExpression expr) => expr switch {
        Literal l => PrintLiteralValue(l.Value),
        PropertyAccess pa => pa.Name,
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