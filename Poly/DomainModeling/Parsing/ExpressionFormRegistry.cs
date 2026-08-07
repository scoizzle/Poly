using Poly.Grammar;

namespace Poly.DomainModeling.Parsing;

/// <summary>
/// E1 open-form registry: packs register primary expression forms (e.g. <c>Now</c>,
/// <c>12 days</c>) without editing core recursive-descent precedence loops.
/// Tried before built-in primary handling; first success wins; fail-closed on ambiguous double match is pack responsibility (register disjoint shapes).
/// </summary>
public sealed class ExpressionFormRegistry {
    private readonly List<IExpressionPrimaryForm> _forms = [];
    private readonly List<Action<Grammar<DslTokenKind>>> _grammarContributors = [];

    public ExpressionFormRegistry() {
    }

    public ExpressionFormRegistry(ExpressionFormRegistry source) {
        ArgumentNullException.ThrowIfNull(source);
        _forms.AddRange(source._forms);
        _grammarContributors.AddRange(source._grammarContributors);
    }

    /// <summary>Registers a pack-owned primary form. Order is try order.</summary>
    public void Register(IExpressionPrimaryForm form) {
        ArgumentNullException.ThrowIfNull(form);
        _forms.Add(form);
    }

    /// <summary>Optional grammar patterns for pack primaries (documentation + Matcher probes).</summary>
    public void RegisterGrammarContributor(Action<Grammar<DslTokenKind>> contribute) {
        ArgumentNullException.ThrowIfNull(contribute);
        _grammarContributors.Add(contribute);
    }

    public void ContributeGrammarPatterns(Grammar<DslTokenKind> grammar) {
        ArgumentNullException.ThrowIfNull(grammar);
        foreach (var c in _grammarContributors)
            c(grammar);
    }

    /// <summary>
    /// Attempts pack forms at the current cursor. Returns true and a node when a form
    /// consumes tokens and produces an expression.
    /// </summary>
    public bool TryParsePrimary(IDslParseCursor cursor, DslExpressionParser expressions, out DomainExpression expression) {
        ArgumentNullException.ThrowIfNull(cursor);
        ArgumentNullException.ThrowIfNull(expressions);
        foreach (var form in _forms) {
            if (form.TryParse(cursor, expressions, out expression!))
                return true;
        }
        expression = null!;
        return false;
    }

    internal IReadOnlyList<IExpressionPrimaryForm> Forms => _forms;
}

/// <summary>
/// Pack-owned primary expression form. Must leave the cursor unchanged when returning false.
/// </summary>
public interface IExpressionPrimaryForm {
    /// <summary>
    /// When the form matches at <paramref name="cursor"/>, advance the cursor and set
    /// <paramref name="expression"/>. Use <paramref name="expressions"/> for nested
    /// subexpressions (e.g. parenthesized bodies). Return false without consuming.
    /// </summary>
    bool TryParse(IDslParseCursor cursor, DslExpressionParser expressions, out DomainExpression expression);
}