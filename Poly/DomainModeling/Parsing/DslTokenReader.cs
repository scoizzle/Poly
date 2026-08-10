using Poly.Grammar;

namespace Poly.DomainModeling.Parsing;

/// <summary>
/// Product DSL scanner on the buffered reader base. The base owns buffering +
/// committed position (Peek/Consume); this type owns the physical decoding seam:
/// char navigation, line/column tracking, comments, strings, numbers, and the
/// keyword map. Positions are tracked here, on the language side.
/// </summary>
public sealed class DslTokenReader : BufferedTokenReader<DslToken, DslTokenKind> {
    private readonly string _text;
    private int _pos;
    private int _line = 1;
    private int _col = 1;

    public DslTokenReader(string text) => _text = text;

    public override bool EndOfStream(DslTokenKind kind) => kind == DslTokenKind.EndOfFile;

    protected override DslToken ScanNextToken() {
        SkipWhitespaceAndComments();

        if (_pos >= _text.Length)
            return Make(DslTokenKind.EndOfFile, "");

        var ch = PeekChar();

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

        if (ch == '-' && PeekChar(1) == '>') return TwoChar("->", DslTokenKind.Arrow);
        if (ch == '>' && PeekChar(1) == '=') return TwoChar(">=", DslTokenKind.Gte);
        if (ch == '<' && PeekChar(1) == '=') return TwoChar("<=", DslTokenKind.Lte);
        if (ch == '=' && PeekChar(1) == '=') return TwoChar("==", DslTokenKind.Eq);
        if (ch == '!' && PeekChar(1) == '=') return TwoChar("!=", DslTokenKind.Neq);

        if (ch == '>') return Advance(DslTokenKind.Gt);
        if (ch == '<') return Advance(DslTokenKind.Lt);
        if (ch == '+') return Advance(DslTokenKind.Plus);
        if (ch == '-') return Advance(DslTokenKind.Minus);
        if (ch == '*') return Advance(DslTokenKind.Star);
        if (ch == '/') return Advance(DslTokenKind.Slash);

        if (ch == '"')
            return ScanString();

        if (char.IsDigit(ch))
            return ScanNumber();

        if (char.IsLetter(ch) || ch == '_') {
            var word = ScanWord();
            return Make(WordToKind(word), word);
        }

        throw GrammarError.Error($"Unexpected character '{ch}'", _line, _col);
    }

    private DslToken Make(DslTokenKind kind, string text) => new(kind, text, _line, _col);

    private DslToken Advance(DslTokenKind kind) {
        var text = PeekChar().ToString();
        AdvanceChar();
        return Make(kind, text);
    }

    private DslToken TwoChar(string text, DslTokenKind kind) {
        AdvanceChar();
        AdvanceChar();
        return Make(kind, text);
    }

    private void SkipWhitespaceAndComments() {
        while (_pos < _text.Length) {
            var ch = PeekChar();
            if (ch == '\n' || ch == '\r' || ch is ' ' or '\t') { AdvanceChar(); continue; }
            if (ch == '/' && PeekChar(1) == '/') {
                while (_pos < _text.Length && PeekChar() != '\n') AdvanceChar();
                continue;
            }
            break;
        }
    }

    private DslToken ScanString() {
        var startLine = _line;
        var startCol = _col;
        AdvanceChar(); // skip opening "
        var sb = new StringBuilder();
        while (_pos < _text.Length) {
            var ch = PeekChar();
            if (ch == '"') { AdvanceChar(); break; }
            if (ch == '\\' && _pos + 1 < _text.Length) {
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
        return new DslToken(DslTokenKind.StringLiteral, sb.ToString(), startLine, startCol);
    }

    private DslToken ScanNumber() {
        var startLine = _line;
        var startCol = _col;
        var sb = new StringBuilder();
        while (_pos < _text.Length && char.IsDigit(PeekChar())) {
            sb.Append(PeekChar());
            AdvanceChar();
        }
        return new DslToken(DslTokenKind.Number, sb.ToString(), startLine, startCol);
    }

    private string ScanWord() {
        var start = _pos;
        while (_pos < _text.Length && (char.IsLetterOrDigit(PeekChar()) || PeekChar() == '_'))
            AdvanceChar();
        return _text[start.._pos];
    }

    private char PeekChar(int ahead = 0) => _pos + ahead < _text.Length ? _text[_pos + ahead] : '\0';

    private void AdvanceChar() {
        if (_pos >= _text.Length) return;
        var ch = _text[_pos];
        _pos++;
        if (ch == '\n') { _line++; _col = 1; }
        else { _col++; }
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
}