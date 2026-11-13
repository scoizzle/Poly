using Poly.Grammar;

namespace Poly.DomainModeling.Language;

/// <summary>
/// DSL parse cursor — a thin wrapper over the buffered reader + matcher.
/// With the reader owning its committed position, the dual-cursor dance is gone:
/// <c>MatchRule</c> peeks via the matcher, <c>Consume</c> commits via the reader.
/// This is the single cursor for both the expression parser and the structure parser.
/// </summary>
public class DslCursor : IDslParseCursor {
    private readonly ITokenReader<DslToken, DslTokenKind> _reader;
    private readonly Matcher<DslToken, DslTokenKind> _matcher;

    public DslCursor(ITokenReader<DslToken, DslTokenKind> reader, Matcher<DslToken, DslTokenKind> matcher) {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
    }

    /// <summary>
    /// Factory ctor so the matcher and cursor share ONE reader instance (the matcher
    /// and cursor read the same committed position). Used when the grammar depends on
    /// the session's tables (DomainResolver).
    /// </summary>
    public DslCursor(
        ITokenReader<DslToken, DslTokenKind> reader,
        Func<ITokenReader<DslToken, DslTokenKind>, Matcher<DslToken, DslTokenKind>> matcherFactory)
        : this(reader, matcherFactory(reader)) {
    }

    public Grammar<DslToken, DslTokenKind> Grammar => _matcher.Grammar;

    public DslToken Current => _reader.Peek(0);

    public void Advance() => _reader.Consume(1);

    public DslToken Expect(DslTokenKind kind) {
        var t = Current;
        if (t.Kind != kind)
            throw Error($"Expected {kind}, got '{Shown(t)}' ({t.Kind})");
        Advance();
        return t;
    }

    public string ExpectIdentifier(DslTokenKind kind, string context) {
        var t = Current;
        if (t.Kind != kind)
            throw Error($"Expected {context}, got '{Shown(t)}'");
        Advance();
        return Shown(t);
    }

    /// <summary>The token's text as a message shows it (<see cref="DslTokenText.Shown"/>).</summary>
    private protected static string Shown(DslToken t) => DslTokenText.Shown(t);

    public bool PeekIs(DslTokenKind kind) => Peek(1).Kind == kind;

    public DslToken Peek(int n = 1) => _reader.Peek(n);

    public MatchResult<DslToken, DslTokenKind>? MatchRule(string ruleName) => _matcher.TryMatch(ruleName);

    public void Consume(MatchResult<DslToken, DslTokenKind> match) => _reader.Consume(match.Consumed);

    /// <summary>
    /// Matches <paramref name="ruleName"/> with the table's failure report and returns the match (the caller
    /// consumes it); a failure throws the table's message at the failing token. Line and column only: offset and
    /// length stay -1 and 0, as every hand-written resolver error has them, until G-17 adds them everywhere.
    /// </summary>
    public MatchResult<DslToken, DslTokenKind> MatchOrThrow(string ruleName) {
        var outcome = _matcher.Match(ruleName);
        if (outcome.Result is { } match)
            return match;
        var failure = outcome.Failure!;
        throw GrammarError.Error(Describe(failure), failure.Token.Line, failure.Token.Col);
    }

    /// <summary>
    /// The DSL's wording of a table-reported failure (the engine never words one). The expected set joins with
    /// " or ", each element by its label or else its kind. The row's template fills in when it has one; else
    /// <c>Expected {expected}, got '{got}'</c>, or <c>Unexpected '{got}'</c> when nothing was expected. The message
    /// then names the kind it got (<c>({gotKind})</c>), as <see cref="Expect"/> does, unless every expected element is
    /// a labelled content element, which reads as <see cref="ExpectIdentifier"/> does. A refusal is its message alone.
    /// </summary>
    internal static string Describe(MatchFailure<DslToken, DslTokenKind> failure) {
        if (failure.Refused)
            return failure.Expand("", Shown);
        var got = Shown(failure.Token);
        var expected = string.Join(" or ", failure.Expected.Select(e => e.Label ?? e.Kind.ToString()).Distinct(StringComparer.Ordinal));
        var detail = failure.Template is not null ? failure.Expand(expected, Shown)
            : failure.Expected.Count == 0 ? $"Unexpected '{got}'"
            : $"Expected {expected}, got '{got}'";
        return failure.Expected.All(e => e.IsContent && e.Label is not null) ? detail : $"{detail} ({failure.Token.Kind})";
    }

    public Exception Error(string message) {
        var t = Current;
        return GrammarError.Error(message, t.Line, t.Col);
    }

    private bool _inPropertyInitializerValue;
    public bool InPropertyInitializerValue {
        get => _inPropertyInitializerValue;
        set => _inPropertyInitializerValue = value;
    }
}

/// <summary>
/// Cursor surface consumed by the DSL expression parser.
/// Content/position come from the language token.
/// </summary>
public interface IDslParseCursor {
    DslToken Current { get; }
    void Advance();
    DslToken Expect(DslTokenKind kind);
    string ExpectIdentifier(DslTokenKind kind, string context);
    bool PeekIs(DslTokenKind kind);
    DslToken Peek(int n = 1);
    Grammar<DslToken, DslTokenKind> Grammar { get; }
    MatchResult<DslToken, DslTokenKind>? MatchRule(string ruleName);
    void Consume(MatchResult<DslToken, DslTokenKind> match);
    Exception Error(string message);
    bool InPropertyInitializerValue { get; set; }
}