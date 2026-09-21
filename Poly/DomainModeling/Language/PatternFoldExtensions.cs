using Poly.DomainModeling.Ontology;
using Poly.Grammar;

namespace Poly.DomainModeling.Language;

/// <summary>
/// Binds expression folds to grammar patterns without putting Domain IR in Grammar.
/// </summary>
public static class PatternFoldExtensions {
    public static PatternBuilder<DslToken, DslTokenKind> Fold(
        this PatternBuilder<DslToken, DslTokenKind> builder,
        Func<MatchResult<DslToken, DslTokenKind>, DomainExpression> fold) {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(fold);
        return builder.Payload(fold);
    }

    public static GrammarBuilder<DslToken, DslTokenKind> AttachFold(
        this GrammarBuilder<DslToken, DslTokenKind> builder,
        string ruleName,
        string patternName,
        Func<MatchResult<DslToken, DslTokenKind>, DomainExpression> fold) {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(fold);
        return builder.AttachPayload(ruleName, patternName, fold);
    }

    public static bool TryFoldExpression(
        this MatchResult<DslToken, DslTokenKind> match,
        out DomainExpression expression) {
        ArgumentNullException.ThrowIfNull(match);
        if (match.Pattern?.Payload is Func<MatchResult<DslToken, DslTokenKind>, DomainExpression> fold) {
            expression = fold(match);
            return true;
        }
        expression = null!;
        return false;
    }
}