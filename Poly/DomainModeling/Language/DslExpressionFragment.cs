using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Meaning;
using Poly.DomainModeling.Ontology;
using Poly.Grammar;

using Add = Poly.DomainModeling.Ontology.Add;
using Subtract = Poly.DomainModeling.Ontology.Subtract;

namespace Poly.DomainModeling.Language;

/// <summary>
/// Standalone DSL expression fragment entry: parses one product-DSL expression
/// (e.g. <c>Age &gt;= 18</c>) without a domain document. Fail-closed: empty input
/// throws; tokens after the expression throw. Parse applies no library meaning.
/// <see cref="Resolve"/> applies session folds.
/// </summary>
public static class DslExpressionFragment {
    /// <summary>
    /// Parses <paramref name="expression"/> as a DSL expression fragment. Throws
    /// <see cref="FormatException"/> (via GrammarError) on empty input, invalid syntax,
    /// or trailing tokens. Library folds are not applied.
    /// </summary>
    public static DomainExpression ParseExpressionFragment(string expression) {
        ArgumentNullException.ThrowIfNull(expression);
        return ParseOnThisThread(expression);
    }

    /// <summary>The parse itself; the stack tests run it on a 64 KB thread to prove nothing recurses over expression depth.</summary>
    internal static DomainExpression ParseOnThisThread(string expression) {
        if (string.IsNullOrWhiteSpace(expression))
            throw GrammarError.Error("Expression must not be empty");

        var reader = new DslTokenReader(expression);
        var matcher = new Matcher<DslToken, DslTokenKind>(DslGrammar.Core, reader);
        var cursor = new DslCursor(reader, matcher);
        var parser = new DslExpressionParser(cursor);
        var result = parser.ParseExpression();

        if (cursor.Current.Kind != DslTokenKind.EndOfFile)
            throw GrammarError.Error(
                $"Trailing tokens after expression: '{DslTokenText.Shown(cursor.Current)}'",
                cursor.Current.Line,
                cursor.Current.Col);

        return result;
    }

    /// <summary>
    /// Applies <paramref name="session"/> ident, duration, and binary folds to a
    /// fragment tree. A duration with no claim throws <see cref="FormatException"/>.
    /// </summary>
    public static DomainExpression Resolve(DomainExpression parsed, DomainSession session) {
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentNullException.ThrowIfNull(session);
        return new Resolver(session.ExpressionForms, DslOperatorTable.Core(session.ExpressionForms)).Route(parsed);
    }

    /// <summary>
    /// Throws when a document expression still carries an unclaimed duration.
    /// </summary>
    internal static DomainExpression RejectUnresolvedDuration(DomainExpression parsed) =>
        new Rejector().Route(parsed);

    private sealed class Resolver : DomainExpressionRewriteBase {
        private readonly ExpressionFormRegistry _forms;
        private readonly DslOperatorTable _operators;

        public Resolver(ExpressionFormRegistry forms, DslOperatorTable operators) {
            _forms = forms;
            _operators = operators;
        }

        protected override DomainExpression PropertyAccess(PropertyAccess e) =>
            _forms.TryRewriteIdent(e.Name, out var claimed) ? claimed : e;

        protected override DomainExpression Add(Add e) =>
            _operators.Fold(DslTokenKind.Plus, Route(e.Left), Route(e.Right));

        protected override DomainExpression Subtract(Subtract e) =>
            _operators.Fold(DslTokenKind.Minus, Route(e.Left), Route(e.Right));

        protected override DomainExpression Library(DomainExpression expr) =>
            expr is UnresolvedDuration duration ? Claim(duration) : base.Library(expr);

        private DomainExpression Claim(UnresolvedDuration duration) {
            if (_forms.TryClaimDuration(duration.Amount, duration.Unit, out var claimed))
                return claimed;
            throw GrammarError.Error(
                $"Duration '{duration.Amount} {duration.Unit}' requires uses temporal.");
        }
    }

    private sealed class Rejector : DomainExpressionRewriteBase {
        protected override DomainExpression Library(DomainExpression expr) =>
            expr is UnresolvedDuration duration
                ? throw GrammarError.Error(
                    $"Duration '{duration.Amount} {duration.Unit}' requires uses temporal.")
                : base.Library(expr);
    }
}