using Poly.Grammar;

namespace Poly.Interpretation.CSharp;

/// <summary>
/// C# scanner for the exported subset, on the buffered reader base. The base
/// owns buffering and the committed position (Peek/Consume); this type owns the
/// physical decoding seam: whitespace, comments, preprocessor lines, strings,
/// numbers, and the keyword map. Positions are tracked here, on the language side.
///
/// Strings and chars are decoded to their runtime value (<c>\n</c> becomes a
/// newline inside the token text); the writer encodes from the value, so
/// read/write round-trips through the value, not the spelling.
/// </summary>
public sealed class CSharpTokenizer : BufferedTokenReader<CSharpToken, CSharpTokenKind> {
    private readonly string _text;
    private int _pos;
    private int _line = 1;
    private int _col = 1;

    public CSharpTokenizer(string text) => _text = text ?? throw new ArgumentNullException(nameof(text));

    public override bool EndOfStream(CSharpTokenKind kind) => kind == CSharpTokenKind.EndOfFile;

    protected override CSharpToken ScanNextToken() {
        SkipTrivia();

        if (_pos >= _text.Length)
            return Make(CSharpTokenKind.EndOfFile, "");

        var ch = PeekChar();

        if (ch is '{' or '}' or '(' or ')' or '[' or ']' or ';' or ',' or '.' or ':') {
            AdvanceChar();
            return Make(ch switch {
                '{' => CSharpTokenKind.LBrace,
                '}' => CSharpTokenKind.RBrace,
                '(' => CSharpTokenKind.LParen,
                ')' => CSharpTokenKind.RParen,
                '[' => CSharpTokenKind.LBracket,
                ']' => CSharpTokenKind.RBracket,
                ';' => CSharpTokenKind.Semicolon,
                ',' => CSharpTokenKind.Comma,
                '.' => CSharpTokenKind.Dot,
                _ => CSharpTokenKind.Colon,
            }, ch.ToString());
        }

        switch (ch) {
            case '"': return ScanString();
            case '\'': return ScanChar();
        }

        // Multi-character operators, longest first.
        if (ch == '=' && PeekChar(1) == '>') return Two(CSharpTokenKind.Arrow);
        if (ch == '=' && PeekChar(1) == '=') return Two(CSharpTokenKind.EqEq);
        if (ch == '!' && PeekChar(1) == '=') return Two(CSharpTokenKind.NotEq);
        if (ch == '<' && PeekChar(1) == '=') return Two(CSharpTokenKind.LtEq);
        if (ch == '>' && PeekChar(1) == '=') return Two(CSharpTokenKind.GtEq);
        if (ch == '&' && PeekChar(1) == '&') return Two(CSharpTokenKind.AmpAmp);
        if (ch == '|' && PeekChar(1) == '|') return Two(CSharpTokenKind.PipePipe);
        if (ch == '?' && PeekChar(1) == '?') return Two(CSharpTokenKind.QuestionQuestion);
        if (ch == '?' && PeekChar(1) == '.') return Two(CSharpTokenKind.QuestionDot);

        switch (ch) {
            case '=': return One(CSharpTokenKind.Assign);
            case '+': return One(CSharpTokenKind.Plus);
            case '-': return One(CSharpTokenKind.Minus);
            case '*': return One(CSharpTokenKind.Star);
            case '/': return One(CSharpTokenKind.Slash);
            case '%': return One(CSharpTokenKind.Percent);
            case '&': return One(CSharpTokenKind.Amp);
            case '|': return One(CSharpTokenKind.Pipe);
            case '^': return One(CSharpTokenKind.Caret);
            case '~': return One(CSharpTokenKind.Tilde);
            case '!': return One(CSharpTokenKind.Bang);
            case '<': return One(CSharpTokenKind.Lt);
            case '>': return One(CSharpTokenKind.Gt);
            case '?': return One(CSharpTokenKind.Question);
        }

        if (char.IsDigit(ch))
            return ScanNumber();

        if (ch == '@' && IsWordStart(PeekChar(1)))
            return ScanWord(atPrefixed: true);

        if (IsWordStart(ch))
            return ScanWord(atPrefixed: false);

        throw GrammarError.Error($"Unexpected character '{ch}' in C# source", _line, _col);
    }

    private CSharpToken Make(CSharpTokenKind kind, string text) => new(kind, text, _line, _col);

    private CSharpToken One(CSharpTokenKind kind) {
        var text = PeekChar().ToString();
        AdvanceChar();
        return Make(kind, text);
    }

    private CSharpToken Two(CSharpTokenKind kind) {
        var text = PeekChar().ToString() + PeekChar(1);
        AdvanceChar();
        AdvanceChar();
        return Make(kind, text);
    }

    private void SkipTrivia() {
        while (_pos < _text.Length) {
            var ch = PeekChar();
            if (ch is ' ' or '\t' or '\r' or '\n') { AdvanceChar(); continue; }
            if (ch == '/' && PeekChar(1) == '/') {
                while (_pos < _text.Length && PeekChar() != '\n') AdvanceChar();
                continue;
            }
            if (ch == '/' && PeekChar(1) == '*') {
                AdvanceChar();
                AdvanceChar();
                while (_pos < _text.Length && !(PeekChar() == '*' && PeekChar(1) == '/')) AdvanceChar();
                if (_pos < _text.Length) { AdvanceChar(); AdvanceChar(); }
                continue;
            }
            if (ch == '#') {
                while (_pos < _text.Length && PeekChar() != '\n') AdvanceChar();
                continue;
            }
            break;
        }
    }

    private CSharpToken ScanString() {
        var startLine = _line;
        var startCol = _col;
        AdvanceChar(); // opening quote
        var sb = new StringBuilder();
        while (_pos < _text.Length) {
            var ch = PeekChar();
            if (ch == '"') { AdvanceChar(); break; }
            if (ch == '\\' && _pos + 1 < _text.Length) {
                sb.Append(ReadEscape());
                continue;
            }
            sb.Append(ch);
            AdvanceChar();
        }
        return new CSharpToken(CSharpTokenKind.StringLiteral, sb.ToString(), startLine, startCol);
    }

    private CSharpToken ScanChar() {
        var startLine = _line;
        var startCol = _col;
        AdvanceChar(); // opening quote
        var value = "";
        if (_pos < _text.Length) {
            if (PeekChar() == '\\' && _pos + 1 < _text.Length)
                value = ReadEscape();
            else {
                value = PeekChar().ToString();
                AdvanceChar();
            }
        }
        if (_pos < _text.Length && PeekChar() == '\'') AdvanceChar();
        return new CSharpToken(CSharpTokenKind.CharLiteral, value, startLine, startCol);
    }

    private string ReadEscape() {
        AdvanceChar(); // backslash
        var esc = PeekChar();
        AdvanceChar();
        return esc switch {
            'n' => "\n",
            'r' => "\r",
            't' => "\t",
            '0' => "\0",
            'a' => "\a",
            'b' => "\b",
            'f' => "\f",
            'v' => "\v",
            '\\' => "\\",
            '\'' => "'",
            '"' => "\"",
            'u' => ReadHex(4),
            'x' => ReadHex(4),
            _ => esc.ToString(),
        };
    }

    private string ReadHex(int digits) {
        var sb = new StringBuilder();
        for (var i = 0; i < digits && _pos < _text.Length && Uri.IsHexDigit(PeekChar()); i++) {
            sb.Append(PeekChar());
            AdvanceChar();
        }
        return sb.Length == 0 ? "" : ((char)Convert.ToInt32(sb.ToString(), 16)).ToString();
    }

    private CSharpToken ScanNumber() {
        var startLine = _line;
        var startCol = _col;
        var sb = new StringBuilder();
        while (_pos < _text.Length && (char.IsDigit(PeekChar()) || PeekChar() == '_')) {
            sb.Append(PeekChar());
            AdvanceChar();
        }
        if (_pos + 1 < _text.Length && PeekChar() == '.' && char.IsDigit(PeekChar(1))) {
            sb.Append('.');
            AdvanceChar();
            while (_pos < _text.Length && (char.IsDigit(PeekChar()) || PeekChar() == '_')) {
                sb.Append(PeekChar());
                AdvanceChar();
            }
        }
        if (_pos < _text.Length && (PeekChar() is 'e' or 'E')
            && (char.IsDigit(PeekChar(1)) || ((PeekChar(1) is '+' or '-') && char.IsDigit(PeekChar(2))))) {
            sb.Append(PeekChar());
            AdvanceChar();
            if (PeekChar() is '+' or '-') { sb.Append(PeekChar()); AdvanceChar(); }
            while (_pos < _text.Length && char.IsDigit(PeekChar())) {
                sb.Append(PeekChar());
                AdvanceChar();
            }
        }
        // Numeric suffixes (L, U, UL, f, d, m).
        while (_pos < _text.Length && PeekChar() is 'f' or 'F' or 'd' or 'D' or 'm' or 'M' or 'u' or 'U' or 'l' or 'L') {
            sb.Append(PeekChar());
            AdvanceChar();
        }
        return new CSharpToken(CSharpTokenKind.NumberLiteral, sb.ToString(), startLine, startCol);
    }

    private CSharpToken ScanWord(bool atPrefixed) {
        var startLine = _line;
        var startCol = _col;
        if (atPrefixed) AdvanceChar(); // '@'
        var start = _pos;
        while (_pos < _text.Length && IsWordPart(PeekChar()))
            AdvanceChar();
        var word = _text[start.._pos];
        return new CSharpToken(WordToKind(word, atPrefixed), word, startLine, startCol);
    }

    private static bool IsWordStart(char ch) => char.IsLetter(ch) || ch == '_';

    private static bool IsWordPart(char ch) => char.IsLetterOrDigit(ch) || ch == '_';

    /// <summary>
    /// Keyword map for the exported subset. An <c>@</c>-prefixed word is always
    /// an identifier (its text is the name without the prefix, matching the
    /// writer's keyword escaping).
    /// </summary>
    private static CSharpTokenKind WordToKind(string word, bool atPrefixed) {
        if (atPrefixed) return CSharpTokenKind.Identifier;
        return word switch {
            "using" => CSharpTokenKind.KwUsing,
            "namespace" => CSharpTokenKind.KwNamespace,
            "class" => CSharpTokenKind.KwClass,
            "record" => CSharpTokenKind.KwRecord,
            "interface" => CSharpTokenKind.KwInterface,
            "enum" => CSharpTokenKind.KwEnum,
            "public" => CSharpTokenKind.KwPublic,
            "private" => CSharpTokenKind.KwPrivate,
            "protected" => CSharpTokenKind.KwProtected,
            "internal" => CSharpTokenKind.KwInternal,
            "static" => CSharpTokenKind.KwStatic,
            "const" => CSharpTokenKind.KwConst,
            "readonly" => CSharpTokenKind.KwReadonly,
            "volatile" => CSharpTokenKind.KwVolatile,
            "override" => CSharpTokenKind.KwOverride,
            "async" => CSharpTokenKind.KwAsync,
            "await" => CSharpTokenKind.KwAwait,
            "new" => CSharpTokenKind.KwNew,
            "this" => CSharpTokenKind.KwThis,
            "base" => CSharpTokenKind.KwBase,
            "return" => CSharpTokenKind.KwReturn,
            "throw" => CSharpTokenKind.KwThrow,
            "if" => CSharpTokenKind.KwIf,
            "else" => CSharpTokenKind.KwElse,
            "while" => CSharpTokenKind.KwWhile,
            "do" => CSharpTokenKind.KwDo,
            "for" => CSharpTokenKind.KwFor,
            "foreach" => CSharpTokenKind.KwForeach,
            "in" => CSharpTokenKind.KwIn,
            "switch" => CSharpTokenKind.KwSwitch,
            "case" => CSharpTokenKind.KwCase,
            "default" => CSharpTokenKind.KwDefault,
            "try" => CSharpTokenKind.KwTry,
            "catch" => CSharpTokenKind.KwCatch,
            "finally" => CSharpTokenKind.KwFinally,
            "goto" => CSharpTokenKind.KwGoto,
            "break" => CSharpTokenKind.KwBreak,
            "continue" => CSharpTokenKind.KwContinue,
            "get" => CSharpTokenKind.KwGet,
            "set" => CSharpTokenKind.KwSet,
            "init" => CSharpTokenKind.KwInit,
            "var" => CSharpTokenKind.KwVar,
            "void" => CSharpTokenKind.KwVoid,
            "typeof" => CSharpTokenKind.KwTypeof,
            "is" => CSharpTokenKind.KwIs,
            "as" => CSharpTokenKind.KwAs,
            "null" => CSharpTokenKind.KwNull,
            "true" => CSharpTokenKind.KwTrue,
            "false" => CSharpTokenKind.KwFalse,
            "bool" => CSharpTokenKind.KwBool,
            "byte" => CSharpTokenKind.KwByte,
            "sbyte" => CSharpTokenKind.KwSbyte,
            "short" => CSharpTokenKind.KwShort,
            "ushort" => CSharpTokenKind.KwUshort,
            "int" => CSharpTokenKind.KwInt,
            "uint" => CSharpTokenKind.KwUint,
            "long" => CSharpTokenKind.KwLong,
            "ulong" => CSharpTokenKind.KwUlong,
            "float" => CSharpTokenKind.KwFloat,
            "double" => CSharpTokenKind.KwDouble,
            "decimal" => CSharpTokenKind.KwDecimal,
            "char" => CSharpTokenKind.KwChar,
            "string" => CSharpTokenKind.KwString,
            "object" => CSharpTokenKind.KwObject,
            _ => CSharpTokenKind.Identifier,
        };
    }

    private char PeekChar(int ahead = 0) => _pos + ahead < _text.Length ? _text[_pos + ahead] : '\0';

    private void AdvanceChar() {
        if (_pos >= _text.Length) return;
        var ch = _text[_pos];
        _pos++;
        if (ch == '\n') { _line++; _col = 1; }
        else { _col++; }
    }
}