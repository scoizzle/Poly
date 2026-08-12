using Poly.Grammar;

namespace Poly.DomainModeling.Parsing;

/// <summary>
/// DSL parse cursor — a thin wrapper over the buffered reader + matcher.
/// With the reader owning its committed position, the dual-cursor dance is gone:
/// <c>MatchRule</c> peeks via the matcher, <c>Consume</c> commits via the reader.
/// This is the single cursor for both the expression parser and the structure parser.
/// </summary>
public class DslCursor : IDslParseCursor {
    private readonly DslTokenReader _reader;
    private readonly Matcher<DslToken, DslTokenKind> _matcher;
    private bool _inWhereBody;

    public DslCursor(DslTokenReader reader, Matcher<DslToken, DslTokenKind> matcher) {
        _reader = reader;
        _matcher = matcher;
    }

    /// <summary>
    /// Factory ctor so the matcher and cursor share ONE reader instance (the matcher
    /// and cursor read the same committed position). Used when the grammar depends on
    /// parser inputs (PolyDslParser).
    /// </summary>
    public DslCursor(DslTokenReader reader, Func<DslTokenReader, Matcher<DslToken, DslTokenKind>> matcherFactory)
        : this(reader, matcherFactory(reader)) {
    }

    public DslToken Current => _reader.Peek(0);

    public void Advance() => _reader.Consume(1);

    public DslToken Expect(DslTokenKind kind) {
        var t = Current;
        if (t.Kind != kind)
            throw Error($"Expected {kind}, got '{t.Text}' ({t.Kind})");
        Advance();
        return t;
    }

    public string ExpectIdentifier(DslTokenKind kind, string context) {
        var t = Current;
        if (t.Kind != kind)
            throw Error($"Expected {context}, got '{t.Text}'");
        Advance();
        return t.Text;
    }

    public bool PeekIs(DslTokenKind kind) => Peek(1).Kind == kind;

    public DslToken Peek(int n = 1) => _reader.Peek(n);

    public MatchResult<DslToken, DslTokenKind>? MatchRule(string ruleName) => _matcher.TryMatch(ruleName);

    public void Consume(MatchResult<DslToken, DslTokenKind> match) => _reader.Consume(match.Consumed);

    public Exception Error(string message) {
        var t = Current;
        return GrammarError.Error(message, t.Line, t.Col);
    }

    public bool InWhereBody {
        get => _inWhereBody;
        set => _inWhereBody = value;
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
    MatchResult<DslToken, DslTokenKind>? MatchRule(string ruleName);
    void Consume(MatchResult<DslToken, DslTokenKind> match);
    Exception Error(string message);
    bool InWhereBody { get; set; }
    bool InPropertyInitializerValue { get; set; }
}