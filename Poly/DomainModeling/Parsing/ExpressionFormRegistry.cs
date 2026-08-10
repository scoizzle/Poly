using Poly.Grammar;

namespace Poly.DomainModeling.Parsing;

/// <summary>
/// Pack-owned primary expression form. Must leave the cursor
/// unchanged when returning false.
/// </summary>
public interface IExpressionPrimaryForm {
    bool TryParse(IDslParseCursor cursor, DslExpressionParser expressions, out DomainExpression expression);
}

/// <summary>
/// E1 open-form registry: packs register primary expression forms without
/// editing core precedence layers. Tried before built-in primary handling;
/// first success wins; fail-closed on ambiguous double match is pack responsibility.
/// </summary>
public sealed class ExpressionFormRegistry {
    private readonly List<IExpressionPrimaryForm> _forms = [];
    private readonly List<Action<Grammar<DslToken, DslTokenKind>>> _grammarContributors = [];

    public ExpressionFormRegistry() {
    }

    public ExpressionFormRegistry(ExpressionFormRegistry source) {
        ArgumentNullException.ThrowIfNull(source);
        _forms.AddRange(source._forms);
        _grammarContributors.AddRange(source._grammarContributors);
    }

    public void Register(IExpressionPrimaryForm form) {
        ArgumentNullException.ThrowIfNull(form);
        _forms.Add(form);
    }

    /// <summary>Optional grammar patterns for pack primaries (documentation + Matcher probes).</summary>
    public void RegisterGrammarContributor(Action<Grammar<DslToken, DslTokenKind>> contribute) {
        ArgumentNullException.ThrowIfNull(contribute);
        _grammarContributors.Add(contribute);
    }

    public void ContributeGrammarPatterns(Grammar<DslToken, DslTokenKind> grammar) {
        ArgumentNullException.ThrowIfNull(grammar);
        foreach (var c in _grammarContributors)
            c(grammar);
    }

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
}