using Poly.Grammar;

namespace Poly.DomainModeling.Parsing;

using Token = Token<DslTokenKind>;

/// <summary>
/// Shared head-token cursor for structure and expression parsers (dual-cursor + Matcher).
/// </summary>
public interface IDslParseCursor {
    Token Current { get; }
    void Advance();
    Token Expect(DslTokenKind kind);
    string ExpectIdentifier(DslTokenKind kind, string context);
    bool PeekIs(DslTokenKind kind);
    Token Peek(int n = 1);
    MatchResult<DslTokenKind>? MatchRule(string ruleName);
    Exception Error(string message);
    bool InWhereBody { get; set; }
}