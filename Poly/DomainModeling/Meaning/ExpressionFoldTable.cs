using System.Globalization;

using Poly.DomainModeling.Ontology;
using Poly.Grammar;

namespace Poly.DomainModeling.Meaning;

/// <summary>
/// (rule, pattern) → expression IR. Held by a domain session; Grammar stays IR-free.
/// Patterns that need follow-on RD (ident, group, not) are not registered.
/// </summary>
public sealed class ExpressionFoldTable {
    private readonly Dictionary<(string Rule, string Pattern), Func<MatchResult<DslToken, DslTokenKind>, DomainExpression>> _folds =
        new();

    public static ExpressionFoldTable Core() {
        var table = new ExpressionFoldTable();
        table.Register("expr-primary", "number", FoldNumber);
        table.Register("expr-primary", "string", m => DomainExpression.Literal(Text(m)));
        table.Register("expr-primary", "true", _ => DomainExpression.Literal(true));
        table.Register("expr-primary", "false", _ => DomainExpression.Literal(false));
        table.Register("expr-primary", "null", _ => DomainExpression.Literal(null));
        table.Register("expr-primary-no-not", "number", FoldNumber);
        table.Register("expr-primary-no-not", "string", m => DomainExpression.Literal(Text(m)));
        table.Register("expr-primary-no-not", "true", _ => DomainExpression.Literal(true));
        table.Register("expr-primary-no-not", "false", _ => DomainExpression.Literal(false));
        table.Register("expr-primary-no-not", "null", _ => DomainExpression.Literal(null));
        return table;
    }

    public void Register(string rule, string pattern, Func<MatchResult<DslToken, DslTokenKind>, DomainExpression> fold) {
        ArgumentException.ThrowIfNullOrWhiteSpace(rule);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        ArgumentNullException.ThrowIfNull(fold);
        var key = (rule, pattern);
        if (!_folds.TryAdd(key, fold))
            throw new InvalidOperationException($"A fold for '{rule}/{pattern}' is already registered.");
    }

    public bool TryFold(string rule, MatchResult<DslToken, DslTokenKind> match, out DomainExpression expression) {
        ArgumentException.ThrowIfNullOrWhiteSpace(rule);
        ArgumentNullException.ThrowIfNull(match);
        if (_folds.TryGetValue((rule, match.PatternName), out var fold)) {
            expression = fold(match);
            return true;
        }
        expression = null!;
        return false;
    }

    private static string Text(MatchResult<DslToken, DslTokenKind> match) =>
        match.Tokens.Count > 0
            ? match.Tokens[0].Text
            : throw new InvalidOperationException($"Fold '{match.PatternName}' matched no tokens.");

    private static DomainExpression FoldNumber(MatchResult<DslToken, DslTokenKind> match) {
        var numText = Text(match);
        if (long.TryParse(numText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longVal))
            return DomainExpression.Literal(longVal);
        if (double.TryParse(numText, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleVal))
            return DomainExpression.Literal(doubleVal);
        return DomainExpression.Literal(numText);
    }
}