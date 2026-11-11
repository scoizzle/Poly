using System.Globalization;

using Poly.DomainModeling.Ontology;
using Poly.Grammar;

using Action = Poly.DomainModeling.Ontology.Action;
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
public sealed partial class DomainDslPrinter {
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
            if (string.Equals(extensionId, ExtensionCatalog.TemporalId, StringComparison.Ordinal))
                continue;
            var seeded = domain.ImportedContracts.FirstOrDefault(contract =>
                string.Equals(contract.UsesId, extensionId, StringComparison.Ordinal));
            if (seeded?.Pin is { Length: > 0 } pin) {
                _sb.AppendLine(_printTable.Print(
                    "uses",
                    "pinned",
                    fills: new Dictionary<string, string>(StringComparer.Ordinal) {
                        ["id"] = extensionId,
                        ["version"] = pin,
                    }));
                continue;
            }
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

        foreach (var function in domain.Functions) {
            PrintFunction(function);
            _sb.AppendLine();
        }

        // A contract seeded by `uses <id>` is the export-package reference, already printed
        // on the uses line; only body-authored blocks print here.
        foreach (var contract in domain.ImportedContracts
                     .Where(c => c.UsesId is null || !domain.Extensions.Contains(c.UsesId, StringComparer.Ordinal))
                     .OrderBy(c => c.Name, StringComparer.Ordinal)) {
            PrintContract(contract);
            _sb.AppendLine();
        }

        foreach (var binding in domain.ContractBindings.OrderBy(b => b.Name, StringComparer.Ordinal)) {
            PrintBinding(binding);
            _sb.AppendLine();
        }

        return _sb.ToString().TrimEnd() + "\n";
    }

    // The parser reads a bare identifier or a string literal here; anything the scanner would
    // not read back as one identifier (a keyword, a digit-first or punctuated value) is quoted.
    private static string IdentifierOrStringLiteral(string value) =>
        DslTokenReader.IsIdentifier(value) ? value : $"\"{EscapeStringLiteral(value)}\"";

    /// <summary>
    /// Canonical identity for one action fragment as it is lowered into a dispatched
    /// stage body: the action's effects, require gates, and parameter list, plus the
    /// source stage's policies and OnExit effects (all inlined into that one body).
    /// Two fragments with the same text lower to the same body and may share a
    /// stage-set guard. The action name and source-stage name are excluded — they
    /// are the group key and the guard, not part of the lowered body.
    /// </summary>
    internal string PrintActionFragmentKey(Action action, Stage? sourceStage) {
        _sb.Clear();
        _sb.Append("params:");
        foreach (var p in action.Parameters)
            _sb.Append(p.Name).Append(':').Append(p.Type.TypeName).Append(',');
        _sb.AppendLine();
        _sb.Append("require:");
        foreach (var policy in action.Policies)
            _sb.Append(policy.Name).Append(',');
        _sb.AppendLine();
        _sb.AppendLine("effects:");
        foreach (var effect in action.Effects)
            PrintEffect(effect, "  ");
        _sb.AppendLine("stagePolicies:");
        if (sourceStage is not null)
            foreach (var policy in sourceStage.Policies)
                _sb.Append(policy.Name).Append(',');
        _sb.AppendLine("onExit:");
        if (sourceStage is not null)
            foreach (var effect in sourceStage.OnExitEffects)
                PrintEffect(effect, "  ");
        return _sb.ToString();
    }

    /// <summary>
    /// Prints a single expression as DSL text (e.g. <c>Height &gt; 100</c>) — the same spelling
    /// the parser accepts. Readers must not surface the AST record shape.
    /// </summary>
    public string PrintExpressionText(DomainExpression expression) {
        ArgumentNullException.ThrowIfNull(expression);
        return PrintExpression(expression);
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

    private static string PrintLiteral(Literal literal) {
        if (literal.Value is null) return "null";
        if (literal.Value is bool b) return b ? "true" : "false";
        if (literal.Value is string s) return $"\"{EscapeStringLiteral(s)}\"";
        if (literal.Value is long l) return l.ToString(CultureInfo.InvariantCulture);
        if (literal.Value is decimal m) return m.ToString(CultureInfo.InvariantCulture);
        if (literal.Value is double d) return d.ToString("0.0###############", CultureInfo.InvariantCulture);
        return Convert.ToString(literal.Value, CultureInfo.InvariantCulture) ?? "null";
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

    /// <summary>
    /// <see cref="RequiredConstraint"/> is not a DSL word. A property is required
    /// unless it is marked <c>optional</c>, so the printer omits that node.
    /// </summary>
    private void AppendPrintedConstraints(IEnumerable<Constraint> constraints) {
        foreach (var c in constraints) {
            if (ConstraintKinds.Of(c) is { PrintedInDsl: false })
                continue;
            _sb.Append(' ');
            _sb.Append(PrintConstraint(c));
        }
    }

    private static string PrintLiteralValue(object? value) => value switch {
        null => "null",
        true => "true",
        false => "false",
        string s => $"\"{EscapeStringLiteral(s)}\"",
        double d => d.ToString("0.0###############", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null",
    };
}