using System.Text;

using Poly.Grammar;

namespace Poly.DomainModeling.Parsing;

/// <summary>
/// Product DSL token writer — the inverse of <see cref="DslTokenReader"/>'s
/// whitespace/comment skip. Inserts a space between two **word** tokens (Identifier,
/// Number, StringLiteral, and every keyword kind) and after
/// <see cref="DslTokenKind.Colon"/> before a word; punctuation attaches
/// (<c>(</c> <c>)</c> <c>{</c> <c>}</c> <c>,</c> <c>.</c>). Binders never emit spaces.
/// </summary>
public sealed class DslTokenWriter : ITokenWriter<DslTokenKind> {
    private readonly Func<DslTokenKind, string> _canonical;
    private readonly StringBuilder _sb = new();
    private DslTokenKind _lastKind;
    private bool _lastIsWord;
    private bool _hasLast;

    public DslTokenWriter() : this(DslGrammar.CanonicalText) { }

    public DslTokenWriter(Func<DslTokenKind, string> canonical) {
        _canonical = canonical ?? throw new ArgumentNullException(nameof(canonical));
    }

    public void Write(DslTokenKind kind) => WriteToken(kind, _canonical(kind), IsWord(kind));

    public void Write(DslTokenKind kind, string text) {
        if (kind == default) {
            // Raw append (Grammar PrintContext.Emit(string)): word-like content.
            Append(text, isWord: true);
            _lastKind = default;
            return;
        }
        WriteToken(kind, text, IsWord(kind));
    }

    public string ToText() => _sb.ToString();

    private void WriteToken(DslTokenKind kind, string text, bool isWord) {
        Append(text, isWord);
        _lastKind = kind;
    }

    private void Append(string text, bool isWord) {
        if (_hasLast && NeedSpace(_lastKind, _lastIsWord, isWord))
            _sb.Append(' ');
        _sb.Append(text);
        _lastIsWord = isWord;
        _hasLast = true;
    }

    private static bool NeedSpace(DslTokenKind lastKind, bool lastIsWord, bool nextIsWord) =>
        (lastIsWord && nextIsWord) || (lastKind == DslTokenKind.Colon && nextIsWord);

    private static bool IsWord(DslTokenKind kind) => kind switch {
        DslTokenKind.Identifier or DslTokenKind.Number or DslTokenKind.StringLiteral => true,
        DslTokenKind.Colon or DslTokenKind.Comma or DslTokenKind.Dot
            or DslTokenKind.LParen or DslTokenKind.RParen
            or DslTokenKind.LBrace or DslTokenKind.RBrace
            or DslTokenKind.LBracket or DslTokenKind.RBracket => false,
        _ => true, // every keyword kind
    };
}