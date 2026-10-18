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
            case '"': return PeekChar(1) == '"' && PeekChar(2) == '"' ? ScanRawString() : ScanString();
            case '\'': return ScanChar();
            case '$': return ScanInterpolatedString();
        }

        // Multi-character operators, longest first.
        if (ch == '?' && PeekChar(1) == '?' && PeekChar(2) == '=') return Three(CSharpTokenKind.QuestionQuestionAssign);
        if (ch == '=' && PeekChar(1) == '>') return Two(CSharpTokenKind.Arrow);
        if (ch == '=' && PeekChar(1) == '=') return Two(CSharpTokenKind.EqEq);
        if (ch == '!' && PeekChar(1) == '=') return Two(CSharpTokenKind.NotEq);
        if (ch == '<' && PeekChar(1) == '=') return Two(CSharpTokenKind.LtEq);
        if (ch == '>' && PeekChar(1) == '=') return Two(CSharpTokenKind.GtEq);
        if (ch == '&' && PeekChar(1) == '&') return Two(CSharpTokenKind.AmpAmp);
        if (ch == '|' && PeekChar(1) == '|') return Two(CSharpTokenKind.PipePipe);
        if (ch == '?' && PeekChar(1) == '?') return Two(CSharpTokenKind.QuestionQuestion);
        if (ch == '?' && PeekChar(1) == '.') return Two(CSharpTokenKind.QuestionDot);
        if (ch == '+' && PeekChar(1) == '=') return Two(CSharpTokenKind.PlusAssign);
        if (ch == '-' && PeekChar(1) == '=') return Two(CSharpTokenKind.MinusAssign);
        if (ch == '*' && PeekChar(1) == '=') return Two(CSharpTokenKind.StarAssign);
        if (ch == '/' && PeekChar(1) == '=') return Two(CSharpTokenKind.SlashAssign);
        if (ch == '%' && PeekChar(1) == '=') return Two(CSharpTokenKind.PercentAssign);
        if (ch == '&' && PeekChar(1) == '=') return Two(CSharpTokenKind.AmpAssign);
        if (ch == '|' && PeekChar(1) == '=') return Two(CSharpTokenKind.PipeAssign);
        if (ch == '^' && PeekChar(1) == '=') return Two(CSharpTokenKind.CaretAssign);

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

        if (ch == '@' && PeekChar(1) == '"')
            return ScanVerbatimString();

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

    private CSharpToken Three(CSharpTokenKind kind) {
        var text = PeekChar().ToString() + PeekChar(1) + PeekChar(2);
        AdvanceChar();
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
        var raw = ScanStringBody();
        if (_pos < _text.Length) AdvanceChar(); // closing quote
        return new CSharpToken(CSharpTokenKind.StringLiteral, DecodeStringBody(raw), startLine, startCol);
    }

    /// <summary>
    /// Scans a verbatim string <c>@"…"</c>. Verbatim content decodes no escapes —
    /// a backslash is literal — and <c>""</c> is a single quote. The token text is
    /// the runtime value, matching the ordinary string path, so the writer's
    /// ordinary escaping prints it back.
    /// </summary>
    private CSharpToken ScanVerbatimString() {
        var startLine = _line;
        var startCol = _col;
        AdvanceChar(); // '@'
        AdvanceChar(); // opening quote
        var sb = new StringBuilder();
        var closed = false;
        while (_pos < _text.Length) {
            if (PeekChar() == '"') {
                if (PeekChar(1) == '"') {
                    sb.Append('"');
                    AdvanceChar();
                    AdvanceChar();
                    continue;
                }
                AdvanceChar(); // closing quote
                closed = true;
                break;
            }
            sb.Append(PeekChar());
            AdvanceChar();
        }
        if (!closed)
            throw GrammarError.Error("Unterminated verbatim string literal.", startLine, startCol);
        return new CSharpToken(CSharpTokenKind.StringLiteral, sb.ToString(), startLine, startCol);
    }

    /// <summary>
    /// Scans a raw string literal (<c>"""…"""</c>, three or more quotes). Raw
    /// content is verbatim — no escapes — and a shorter run of quotes is literal
    /// text. A multi-line raw string strips the indentation set by the closing
    /// quotes from every content line, as C# does; a line that carries neither
    /// that indentation nor only whitespace fails closed with a positioned error
    /// rather than folding to a wrong value.
    /// </summary>
    private CSharpToken ScanRawString() {
        var startLine = _line;
        var startCol = _col;
        var delimiters = 0;
        while (PeekChar(delimiters) == '"') delimiters++;
        for (var i = 0; i < delimiters; i++) AdvanceChar();

        // The opening quotes end their line (only whitespace after them) exactly
        // when the literal is the multi-line form.
        var afterOpen = 0;
        while (PeekChar(afterOpen) is ' ' or '\t') afterOpen++;
        if (PeekChar(afterOpen) is '\r' or '\n')
            return ScanRawMultiline(delimiters, startLine, startCol);

        return ScanRawSingleLine(delimiters, startLine, startCol);
    }

    private CSharpToken ScanRawSingleLine(int delimiters, int startLine, int startCol) {
        var start = _pos;
        var end = -1;
        for (var i = start; i < _text.Length;) {
            if (_text[i] is '\r' or '\n')
                throw GrammarError.Error("Unterminated raw string literal.", startLine, startCol);
            if (_text[i] != '"') { i++; continue; }
            var run = 0;
            while (i + run < _text.Length && _text[i + run] == '"') run++;
            if (run >= delimiters) {
                if (run != delimiters)
                    throw GrammarError.Error(
                        "Raw string literal does not start with enough quotes to hold this many consecutive quotes.",
                        startLine, startCol);
                end = i;
                break;
            }
            i += run;
        }
        if (end < 0)
            throw GrammarError.Error("Unterminated raw string literal.", startLine, startCol);

        var content = _text[start..end];
        while (_pos < end + delimiters) AdvanceChar();
        return new CSharpToken(CSharpTokenKind.StringLiteral, content, startLine, startCol);
    }

    private CSharpToken ScanRawMultiline(int delimiters, int startLine, int startCol) {
        // Consume the trailing whitespace and the newline that ends the opening line.
        while (PeekChar() is ' ' or '\t') AdvanceChar();
        if (PeekChar() == '\r') AdvanceChar();
        if (PeekChar() != '\n')
            throw GrammarError.Error(
                "Raw string literal opening quotes must be the last non-whitespace on their line.",
                startLine, startCol);
        AdvanceChar(); // newline
        var contentStart = _pos;

        // The closing delimiter is the first run of at least `delimiters` quotes;
        // raw content cannot hold that many consecutive quotes.
        var q = -1;
        var run = 0;
        for (var i = _pos; i < _text.Length;) {
            if (_text[i] != '"') { i++; continue; }
            run = 0;
            while (i + run < _text.Length && _text[i + run] == '"') run++;
            if (run >= delimiters) { q = i; break; }
            i += run;
        }
        if (q < 0)
            throw GrammarError.Error("Unterminated raw string literal.", startLine, startCol);
        if (run != delimiters)
            throw GrammarError.Error(
                "Raw string literal does not start with enough quotes to hold this many consecutive quotes.",
                startLine, startCol);

        // The closing quotes start their line; the whitespace before them is the
        // indentation stripped from every content line.
        var nl = _text.LastIndexOf('\n', q - 1);
        if (nl < 0)
            throw GrammarError.Error(
                "Raw string literal closing quotes must start their own line.", startLine, startCol);
        var indentation = _text[(nl + 1)..q];
        if (!IsRawIndent(indentation))
            throw GrammarError.Error(
                "Raw string literal closing quotes must be the first non-whitespace on their line.",
                startLine, startCol);

        var contentEnd = nl;
        if (contentEnd > contentStart && _text[contentEnd - 1] == '\r') contentEnd--;
        var content = contentEnd > contentStart ? _text[contentStart..contentEnd] : "";
        var value = StripRawIndent(content, indentation, startLine + 1);

        var closeEnd = q + delimiters;
        while (_pos < closeEnd) AdvanceChar();
        return new CSharpToken(CSharpTokenKind.StringLiteral, value, startLine, startCol);
    }

    /// <summary>
    /// Removes the closing quotes' indentation from each content line. A
    /// whitespace-only line loses as much of that indentation as it carries (C#
    /// allows a blank line to be shorter); a content line that does not start
    /// with the exact indentation fails closed.
    /// </summary>
    private static string StripRawIndent(string content, string indentation, int firstContentLine) {
        if (indentation.Length == 0)
            return content;
        var sb = new StringBuilder(content.Length);
        var lineStart = 0;
        var line = firstContentLine;
        while (true) {
            var nl = content.IndexOf('\n', lineStart);
            var lineEnd = nl < 0 ? content.Length : nl;
            var raw = content[lineStart..lineEnd];
            var hasCr = raw.EndsWith('\r');
            var body = hasCr ? raw[..^1] : raw;
            if (body.All(ch => ch is ' ' or '\t')) {
                var strip = 0;
                while (strip < indentation.Length && strip < body.Length && body[strip] == indentation[strip])
                    strip++;
                sb.Append(body[strip..]);
            }
            else if (body.StartsWith(indentation, StringComparison.Ordinal)) {
                sb.Append(body[indentation.Length..]);
            }
            else {
                throw GrammarError.Error(
                    "Raw string literal line does not start with the same whitespace as the closing quotes.",
                    line, 1);
            }
            if (hasCr) sb.Append('\r');
            if (nl < 0) break;
            sb.Append('\n');
            lineStart = nl + 1;
            line++;
        }
        return sb.ToString();
    }

    private static bool IsRawIndent(string text) => text.All(ch => ch is ' ' or '\t');

    /// <summary>
    /// Scans the raw body of a <c>"…"</c> literal: everything up to the next
    /// unescaped quote. Escapes are captured as written (a <c>\"</c> must not end
    /// the literal); decoding is <see cref="DecodeStringBody"/>'s job, so the
    /// interpolated reader can split <c>{…}</c> before decoding the literals.
    /// </summary>
    private string ScanStringBody() {
        var start = _pos;
        while (_pos < _text.Length) {
            var ch = PeekChar();
            if (ch == '"') break;
            // Skip an escape as a unit so an escaped quote is not the terminator.
            // \uXXXX / \xXXXX are still captured whole because the scanner only
            // advances past '\' and the following character.
            if (ch == '\\' && _pos + 1 < _text.Length) {
                AdvanceChar();
                AdvanceChar();
                continue;
            }
            AdvanceChar();
        }
        return _text[start.._pos];
    }

    /// <summary>
    /// Decodes the C# escapes the exported subset accepts inside a string body.
    /// Shared by ordinary and interpolated literals so their semantics do not drift.
    /// </summary>
    internal static string DecodeStringBody(string raw) {
        var sb = new StringBuilder();
        for (var i = 0; i < raw.Length; i++) {
            var ch = raw[i];
            if (ch != '\\' || i + 1 >= raw.Length) {
                sb.Append(ch);
                continue;
            }
            i++;
            sb.Append(raw[i] switch {
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
                'u' => DecodeHex(raw, ref i),
                'x' => DecodeHex(raw, ref i),
                var esc => esc.ToString(),
            });
        }
        return sb.ToString();
    }

    private static string DecodeHex(string raw, ref int i) {
        var sb = new StringBuilder();
        for (var k = 0; k < 4 && i + 1 < raw.Length && Uri.IsHexDigit(raw[i + 1]); k++)
            sb.Append(raw[++i]);
        return sb.Length == 0 ? "" : ((char)Convert.ToInt32(sb.ToString(), 16)).ToString();
    }

    /// <summary>
    /// Scans a plain interpolated string <c>$"…"</c>. Only the bare form is
    /// supported: <c>$@"…"</c>, <c>@$"…"</c>, raw <c>$$"""…"""</c> and a bare
    /// <c>$</c> fail closed. The token text is the raw body between the quotes.
    /// </summary>
    private CSharpToken ScanInterpolatedString() {
        var startLine = _line;
        var startCol = _col;
        AdvanceChar(); // '$'
        if (PeekChar() != '"')
            throw GrammarError.Error(
                "Interpolated strings support only the plain $\"...\" form.", _line, _col);
        AdvanceChar(); // opening quote
        var raw = ScanStringBody();
        if (_pos >= _text.Length)
            throw GrammarError.Error("Unterminated interpolated string.", startLine, startCol);
        AdvanceChar(); // closing quote
        return new CSharpToken(CSharpTokenKind.InterpolatedStringLiteral, raw, startLine, startCol);
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

        // Prefixed integer literals: `0x`/`0X` (hex) and `0b`/`0B` (binary). The
        // prefix belongs to the number, so `0x1F` must not scan as `0` + identifier.
        if (PeekChar() == '0' && PeekChar(1) is 'x' or 'X' or 'b' or 'B') {
            var radix = PeekChar(1) is 'x' or 'X' ? 16 : 2;
            sb.Append(PeekChar());
            AdvanceChar();
            sb.Append(PeekChar());
            AdvanceChar();
            var digits = 0;
            while (_pos < _text.Length && (IsDigitInRadix(PeekChar(), radix) || PeekChar() == '_')) {
                if (PeekChar() != '_') digits++;
                sb.Append(PeekChar());
                AdvanceChar();
            }
            if (digits == 0)
                throw GrammarError.Error(
                    radix == 16
                        ? "Hexadecimal literal '0x' has no digits."
                        : "Binary literal '0b' has no digits.",
                    startLine, startCol);
            // f/F/d/D and m/M are hex digits, so only the integer suffixes apply here.
            while (_pos < _text.Length && PeekChar() is 'u' or 'U' or 'l' or 'L') {
                sb.Append(PeekChar());
                AdvanceChar();
            }
            return new CSharpToken(CSharpTokenKind.NumberLiteral, sb.ToString(), startLine, startCol);
        }

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
            while (_pos < _text.Length && (char.IsDigit(PeekChar()) || PeekChar() == '_')) {
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

    private static bool IsDigitInRadix(char ch, int radix) => radix switch {
        16 => char.IsAsciiHexDigit(ch),
        2 => ch is '0' or '1',
        _ => char.IsAsciiDigit(ch),
    };

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