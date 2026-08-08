using Poly.Grammar;

namespace Poly.DomainModeling.Parsing;

using Token = Poly.Grammar.Token<DslTokenKind>;
using TokenKind = DslTokenKind;

/// <summary>
/// Shared dual-cursor head-token mechanics for grammar-table-driven DSL parsing
/// (mcp-minify N5): hold the head token, Unread it, TryMatch a rule, re-read the
/// head. One implementation for <see cref="PolyDslParser"/>,
/// <see cref="DslExpressionFragment"/>, and the expression parity tests so cursor
/// fixes land in a single place instead of three hand-copies.
///
/// Subclasses call the protected members (<c>MatchRule</c>, <c>Expect</c>,
/// <c>Advance</c>, …) directly; external consumers go through
/// <see cref="IDslParseCursor"/>.
/// </summary>
public abstract class DslParseCursorBase : IDslParseCursor {
    protected readonly DslTokenReader _tokenReader;
    protected Matcher<DslTokenKind> _matcher;
    protected Token _current;
    protected bool _inWhereBody;

    /// <summary>Constructs with a ready matcher (same reader instance).</summary>
    protected DslParseCursorBase(DslTokenReader reader, Matcher<DslTokenKind> matcher)
        : this(reader, _ => matcher) {
    }

    /// <summary>
    /// Constructs with a matcher factory so subclasses whose grammar depends on
    /// parser inputs (e.g. <see cref="PolyDslParser"/>) can build it from the same
    /// reader instance the cursor reads from.
    /// </summary>
    protected DslParseCursorBase(DslTokenReader reader, Func<DslTokenReader, Matcher<DslTokenKind>> matcherFactory) {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(matcherFactory);
        _tokenReader = reader;
        _matcher = matcherFactory(reader);
        _current = _tokenReader.Read();
    }

    public Token Current => _current;

    protected void Advance() => _current = _tokenReader.Read();

    protected Token Expect(TokenKind kind) {
        if (_current.Kind != kind)
            throw Error($"Expected {kind}, got '{_current.Text}' ({_current.Kind})");
        var t = _current;
        Advance();
        return t;
    }

    protected string ExpectIdentifier(TokenKind kind, string context) {
        if (_current.Kind != kind)
            throw Error($"Expected {context}, got '{_current.Text}'");
        var t = _current.Text;
        Advance();
        return t;
    }

    protected bool PeekIs(TokenKind kind) => _tokenReader.Peek(1).Kind == kind;

    protected Token Peek(int n) => _tokenReader.Peek(n);

    /// <summary>
    /// Matcher peeks the reader buffer; this cursor holds the head token.
    /// Unread so Peek(1) is the head, then restore — cursor stays at the match
    /// head (not past the span). Callers Consume when they take the match.
    /// Returns null when no pattern matches; unknown rule names throw from
    /// <see cref="Matcher{TKind}.TryMatch"/> (N3).
    /// </summary>
    protected MatchResult<DslTokenKind>? MatchRule(string ruleName) {
        _tokenReader.Unread(_current);
        var match = _matcher.TryMatch(ruleName);
        _current = _tokenReader.Read();
        return match;
    }

    /// <summary>Advances past all tokens consumed by a match (head stays in sync).</summary>
    protected void Consume(MatchResult<DslTokenKind> match) {
        for (var i = 0; i < match.Consumed; i++)
            Advance();
    }

    protected Exception Error(string message) =>
        new GrammarException(message, _current.Line, _current.Col);

    // ── IDslParseCursor (external consumers) ─────────────────────

    Token IDslParseCursor.Current => Current;
    void IDslParseCursor.Advance() => Advance();
    Token IDslParseCursor.Expect(TokenKind kind) => Expect(kind);
    string IDslParseCursor.ExpectIdentifier(TokenKind kind, string context) => ExpectIdentifier(kind, context);
    bool IDslParseCursor.PeekIs(TokenKind kind) => PeekIs(kind);
    Token IDslParseCursor.Peek(int n) => Peek(n);
    MatchResult<DslTokenKind>? IDslParseCursor.MatchRule(string ruleName) => MatchRule(ruleName);
    Exception IDslParseCursor.Error(string message) => Error(message);
    bool IDslParseCursor.InWhereBody {
        get => _inWhereBody;
        set => _inWhereBody = value;
    }
}