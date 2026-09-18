using System.Globalization;

using Poly.DomainModeling.Ontology;
using Poly.Grammar;

namespace Poly.DomainModeling.Meaning;

/// <summary>
/// (rule, pattern) → expression IR. Held by a domain session; Grammar stays IR-free.
/// Group / not / ident-continuations are table-dispatched in the parser; this
/// table folds closed primaries (literals, bare ident).
/// </summary>
public sealed class ExpressionFoldTable {
    private readonly Dictionary<(string Rule, string Pattern), Func<MatchResult<DslToken, DslTokenKind>, DomainExpression>> _byRule = new();
    private readonly Dictionary<string, Func<MatchResult<DslToken, DslTokenKind>, DomainExpression>> _byPattern =
        new(StringComparer.Ordinal);

    public static ExpressionFoldTable Core() {
        var table = new ExpressionFoldTable();
        table.Register("number", Number);
        table.Register("string", String);
        table.Register("true", True);
        table.Register("false", False);
        table.Register("null", Null);
        table.Register("ident", Ident);
        return table;
    }

    public static DomainExpression Number(MatchResult<DslToken, DslTokenKind> match) => FoldNumber(match);
    public static DomainExpression String(MatchResult<DslToken, DslTokenKind> match) => DomainExpression.Literal(Text(match));
    public static DomainExpression True(MatchResult<DslToken, DslTokenKind> match) => DomainExpression.Literal(true);
    public static DomainExpression False(MatchResult<DslToken, DslTokenKind> match) => DomainExpression.Literal(false);
    public static DomainExpression Null(MatchResult<DslToken, DslTokenKind> match) => DomainExpression.Literal(null);
    public static DomainExpression Ident(MatchResult<DslToken, DslTokenKind> match) => DomainExpression.Property(Text(match));

    public void Register(string pattern, Func<MatchResult<DslToken, DslTokenKind>, DomainExpression> fold) {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        ArgumentNullException.ThrowIfNull(fold);
        if (!_byPattern.TryAdd(pattern, fold))
            throw new InvalidOperationException($"A fold for pattern '{pattern}' is already registered.");
    }

    public void Register(string rule, string pattern, Func<MatchResult<DslToken, DslTokenKind>, DomainExpression> fold) {
        ArgumentException.ThrowIfNullOrWhiteSpace(rule);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        ArgumentNullException.ThrowIfNull(fold);
        var key = (rule, pattern);
        if (!_byRule.TryAdd(key, fold))
            throw new InvalidOperationException($"A fold for '{rule}/{pattern}' is already registered.");
    }

    public bool TryFold(string rule, MatchResult<DslToken, DslTokenKind> match, out DomainExpression expression) {
        ArgumentException.ThrowIfNullOrWhiteSpace(rule);
        ArgumentNullException.ThrowIfNull(match);
        if (_byRule.TryGetValue((rule, match.PatternName), out var fold)
            || _byPattern.TryGetValue(match.PatternName, out fold)) {
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