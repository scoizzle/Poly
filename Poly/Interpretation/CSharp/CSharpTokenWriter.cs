using Poly.Grammar;

namespace Poly.Interpretation.CSharp;

/// <summary>
/// C# token writer — the inverse of <see cref="CSharpTokenizer"/>'s trivia skip.
/// Fixed tokens emit their canonical text with C#-style separators: a space
/// between word tokens, after <c>,</c> and <c>;</c>, before <c>(</c> after control
/// keywords (<c>if</c>, <c>while</c>, <c>foreach</c>, …), and around the spaced
/// operators (<c>=</c>, <c>=&gt;</c>, <c>?</c>, <c>:</c>, <c>??</c>). Everything
/// else attaches (<c>.</c>, <c>(</c>, <c>)</c>, <c>[</c>, <c>]</c>, <c>!</c>).
///
/// Raw emissions (the printer's <c>Emit(string)</c> / named fills) are appended
/// verbatim except that a word-like raw text still gets a separating space from a
/// preceding word-like emission — that is how layout fills (<c>"\n    "</c>) and
/// pre-spaced operator text (<c>" + "</c>) stay literal while
/// <c>public</c> + <c>int</c> still separates.
/// </summary>
public sealed class CSharpTokenWriter : ITokenWriter<CSharpTokenKind> {
    private readonly StringBuilder _sb = new();
    private CSharpTokenKind _lastKind;
    private bool _lastWordLike;
    private bool _lastEndedSpace;
    private bool _hasAny;

    public void Write(CSharpTokenKind kind) {
        if (kind == default)
            return;
        AppendFixed(kind, CSharpGrammar.CanonicalText(kind));
    }

    public void Write(CSharpTokenKind kind, string text) {
        ArgumentNullException.ThrowIfNull(text);
        if (kind == default)
            AppendRaw(text);
        else
            AppendFixed(kind, text);
    }

    public string ToText() => _sb.ToString();

    private void AppendFixed(CSharpTokenKind kind, string text) {
        if (text.Length == 0)
            return;
        if (NeedSpaceBefore(kind))
            _sb.Append(' ');
        _sb.Append(text);
        _lastKind = kind;
        _lastWordLike = IsWordKind(kind);
        _lastEndedSpace = false;
        _hasAny = true;
    }

    private void AppendRaw(string text) {
        if (text.Length == 0)
            return;
        var startsWord = StartsWordLike(text);
        var startsSpace = char.IsWhiteSpace(text[0]);
        if (_hasAny && !_lastEndedSpace && !startsSpace
            && ((_lastWordLike && startsWord)
                || IsSpacedOperator(_lastKind)
                || _lastKind is CSharpTokenKind.Comma or CSharpTokenKind.Semicolon))
            _sb.Append(' ');
        _sb.Append(text);
        _lastKind = default;
        _lastWordLike = EndsWordLike(text);
        _lastEndedSpace = char.IsWhiteSpace(text[^1]);
        _hasAny = true;
    }

    private bool NeedSpaceBefore(CSharpTokenKind kind) {
        if (!_hasAny)
            return false;

        if (kind == CSharpTokenKind.LParen)
            return IsControlKeyword(_lastKind);

        if (IsSpacedOperator(kind))
            return _lastWordLike
                || _lastKind is CSharpTokenKind.RParen or CSharpTokenKind.RBracket
                || _lastKind is CSharpTokenKind.KwTrue or CSharpTokenKind.KwFalse or CSharpTokenKind.KwNull;

        if (IsWordKind(kind))
            return _lastWordLike
                || _lastKind == CSharpTokenKind.Comma
                || _lastKind == CSharpTokenKind.Semicolon
                || (IsSpacedOperator(_lastKind) && _lastKind != CSharpTokenKind.Question);

        if (kind == CSharpTokenKind.Semicolon)
            return false;

        // Content following a spaced operator keeps its distance (casts, invokes,
        // member access and indexers attach).
        if (IsSpacedOperator(_lastKind)
            && kind is not (CSharpTokenKind.RParen or CSharpTokenKind.RBracket or CSharpTokenKind.Semicolon
                or CSharpTokenKind.Comma or CSharpTokenKind.RBrace))
            return true;

        if (_lastKind == CSharpTokenKind.Comma
            && kind is not (CSharpTokenKind.RParen or CSharpTokenKind.RBracket or CSharpTokenKind.RBrace))
            return true;

        return false;
    }

    private static bool IsControlKeyword(CSharpTokenKind kind) => kind
        is CSharpTokenKind.KwIf or CSharpTokenKind.KwWhile or CSharpTokenKind.KwFor
        or CSharpTokenKind.KwForeach or CSharpTokenKind.KwSwitch or CSharpTokenKind.KwCatch
        or CSharpTokenKind.KwUsing;

    private static bool IsSpacedOperator(CSharpTokenKind kind) => kind
        is CSharpTokenKind.Assign or CSharpTokenKind.Arrow or CSharpTokenKind.Question
        or CSharpTokenKind.Colon or CSharpTokenKind.QuestionQuestion;

    internal static bool IsWordKind(CSharpTokenKind kind) => kind
        is CSharpTokenKind.Identifier or CSharpTokenKind.NumberLiteral
        or CSharpTokenKind.StringLiteral or CSharpTokenKind.CharLiteral
        || (kind >= CSharpTokenKind.KwUsing && kind <= CSharpTokenKind.KwObject);

    private static bool StartsWordLike(string text) =>
        text.Length > 0 && (char.IsLetterOrDigit(text[0]) || text[0] is '_' or '@' or '"' or '\'');

    private static bool EndsWordLike(string text) =>
        text.Length > 0 && (char.IsLetterOrDigit(text[^1]) || text[^1] is '_' or '>' or ']' or '?' or '"' or '\'');
}