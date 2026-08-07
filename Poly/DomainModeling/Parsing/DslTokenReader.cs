using System.Text;

using Poly.Grammar;

namespace Poly.DomainModeling.Parsing;

/// <summary>
/// Token reader for the Poly DSL, hosted on the grammar engine's
/// <see cref="Poly.Grammar.StringTokenReader{TKind}"/> base. Produces exactly the
/// product token stream for .poly text — including
/// <c>//</c> line-comment skipping (which the base <c>SkipWhitespace</c> does
/// not handle), two-character operators, string escapes (<c>\"</c>, <c>\\</c>),
/// and the keyword map.
/// </summary>
public sealed class DslTokenReader : StringTokenReader<DslTokenKind> {
    public DslTokenReader(string text) : base(text) { }

    public override bool IsEndOfFile(DslTokenKind kind) => kind == DslTokenKind.EndOfFile;

    protected override Token<DslTokenKind> ScanNextToken() {
        SkipWhitespaceAndComments();

        if (Position >= Text.Length)
            return MakeToken(DslTokenKind.EndOfFile, "");

        var ch = PeekChar();

        // Single-character tokens
        switch (ch) {
            case ':': return Advance(DslTokenKind.Colon);
            case ',': return Advance(DslTokenKind.Comma);
            case '.': return Advance(DslTokenKind.Dot);
            case '(': return Advance(DslTokenKind.LParen);
            case ')': return Advance(DslTokenKind.RParen);
            case '{': return Advance(DslTokenKind.LBrace);
            case '}': return Advance(DslTokenKind.RBrace);
            case '[': return Advance(DslTokenKind.LBracket);
            case ']': return Advance(DslTokenKind.RBracket);
        }

        // Two-character operators
        if (ch == '-' && PeekChar(1) == '>') return TwoChar("->", DslTokenKind.Arrow);
        if (ch == '>' && PeekChar(1) == '=') return TwoChar(">=", DslTokenKind.Gte);
        if (ch == '<' && PeekChar(1) == '=') return TwoChar("<=", DslTokenKind.Lte);
        if (ch == '=' && PeekChar(1) == '=') return TwoChar("==", DslTokenKind.Eq);
        if (ch == '!' && PeekChar(1) == '=') return TwoChar("!=", DslTokenKind.Neq);

        // Single-character operators
        if (ch == '>') return Advance(DslTokenKind.Gt);
        if (ch == '<') return Advance(DslTokenKind.Lt);
        if (ch == '+') return Advance(DslTokenKind.Plus);
        if (ch == '-') return Advance(DslTokenKind.Minus);
        if (ch == '*') return Advance(DslTokenKind.Star);
        if (ch == '/') return Advance(DslTokenKind.Slash);

        // String literals
        if (ch == '"')
            return ScanString();

        // Numbers
        if (char.IsDigit(ch))
            return ScanNumber();

        // Identifiers and keywords
        if (char.IsLetter(ch) || ch == '_') {
            var word = ScanWord();
            return MakeToken(WordToKind(word), word);
        }

        throw Error($"Unexpected character '{ch}'");
    }

    private Token<DslTokenKind> Advance(DslTokenKind kind) {
        var text = PeekChar().ToString();
        AdvanceChar();
        return MakeToken(kind, text);
    }

    private Token<DslTokenKind> TwoChar(string text, DslTokenKind kind) {
        AdvanceChar();
        AdvanceChar();
        return MakeToken(kind, text);
    }

    /// <summary>
    /// Skips whitespace and <c>//</c> line comments. The grammar base
    /// <c>SkipWhitespace</c> intentionally does not consume comments; the DSL
    /// scanner does, matching the legacy tokenizer.
    /// </summary>
    private void SkipWhitespaceAndComments() {
        while (Position < Text.Length) {
            var ch = PeekChar();
            if (ch == '\n') { AdvanceChar(); continue; }
            if (ch == '\r') { AdvanceChar(); continue; }
            if (ch is ' ' or '\t') { AdvanceChar(); continue; }

            if (ch == '/' && PeekChar(1) == '/') {
                while (Position < Text.Length && PeekChar() != '\n')
                    AdvanceChar();
                continue;
            }

            break;
        }
    }

    private Token<DslTokenKind> ScanString() {
        var startLine = Line;
        var startCol = Column;
        AdvanceChar(); // skip opening "
        var sb = new StringBuilder();
        while (Position < Text.Length) {
            var ch = PeekChar();
            if (ch == '"') {
                AdvanceChar();
                break;
            }
            // Support \" and \\ escapes so the printer round-trips embedded quotes.
            if (ch == '\\' && Position + 1 < Text.Length) {
                var next = PeekChar(1);
                if (next is '"' or '\\') {
                    sb.Append(next);
                    AdvanceChar();
                    AdvanceChar();
                    continue;
                }
            }
            sb.Append(ch);
            AdvanceChar();
        }
        return new Token<DslTokenKind>(DslTokenKind.StringLiteral, sb.ToString(), startLine, startCol);
    }

    private Token<DslTokenKind> ScanNumber() {
        var startLine = Line;
        var startCol = Column;
        var sb = new StringBuilder();
        while (Position < Text.Length && char.IsDigit(PeekChar())) {
            sb.Append(PeekChar());
            AdvanceChar();
        }
        return new Token<DslTokenKind>(DslTokenKind.Number, sb.ToString(), startLine, startCol);
    }

    private string ScanWord() {
        var start = Position;
        while (Position < Text.Length && (char.IsLetterOrDigit(PeekChar()) || PeekChar() == '_'))
            AdvanceChar();
        return Text[start..Position];
    }

    private static DslTokenKind WordToKind(string word) => word switch {
        "domain" => DslTokenKind.Domain,
        "entity" => DslTokenKind.Entity,
        "stage" => DslTokenKind.Stage,
        "action" => DslTokenKind.Action,
        "policy" => DslTokenKind.Policy,
        "when" => DslTokenKind.When,
        "require" => DslTokenKind.Require,
        "transition" => DslTokenKind.Transition,
        "to" => DslTokenKind.To,
        "assign" => DslTokenKind.Assign,
        "and" => DslTokenKind.And,
        "or" => DslTokenKind.Or,
        "not" => DslTokenKind.Not,
        "is" => DslTokenKind.Is,
        "true" => DslTokenKind.True,
        "false" => DslTokenKind.False,
        "null" => DslTokenKind.Null,
        "Text" => DslTokenKind.Text,
        "Number" => DslTokenKind.NumberType,
        "Boolean" => DslTokenKind.BooleanType,
        "DateTime" => DslTokenKind.DateTimeType,
        "Date" => DslTokenKind.DateType,
        "required" => DslTokenKind.Required,
        "unique" => DslTokenKind.Unique,
        "range" => DslTokenKind.Range,
        "length" => DslTokenKind.Length,
        "pattern" => DslTokenKind.Pattern,
        "enum" => DslTokenKind.Enum,
        "default" => DslTokenKind.Equals,
        "relationship" => DslTokenKind.Relationship,
        "from" => DslTokenKind.From,
        "one" => DslTokenKind.One,
        "many" => DslTokenKind.Many,
        "owned" => DslTokenKind.Owned,
        "create" => DslTokenKind.Create,
        "in" => DslTokenKind.In,
        "invoke" => DslTokenKind.Invoke,
        "if" => DslTokenKind.If,
        "else" => DslTokenKind.Else,
        "entry" => DslTokenKind.Entry,
        "exit" => DslTokenKind.Exit,
        "delete" => DslTokenKind.Delete,
        "as" => DslTokenKind.As,
        _ => DslTokenKind.Identifier,
    };

    private GrammarException Error(string message) =>
        new(message, Line, Column);
}