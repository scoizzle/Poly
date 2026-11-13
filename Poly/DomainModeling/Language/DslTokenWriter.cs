using Poly.Grammar;

namespace Poly.DomainModeling.Language;

/// <summary>
/// Product DSL token writer — the inverse of <see cref="DslTokenReader"/>'s
/// whitespace/comment skip. Inserts a space between two **word** tokens (Identifier,
/// Number, StringLiteral, raw text, and every keyword kind) and after
/// <see cref="DslTokenKind.Colon"/> before a word; other punctuation attaches
/// (<c>(</c> <c>)</c> <c>,</c> <c>.</c>) except where a rule below spaces it. Binders never emit spaces.
///
/// <para>The rules (G-27) reproduce the hand printer's bytes from tokens:</para>
/// <list type="bullet">
/// <item>a space after <c>,</c> before a word (<c>key(A, B)</c>, <c>(p: T, q: U)</c>);</item>
/// <item>a space after <c>)</c> before a word (<c>initial(S) key(A)</c>, <c>) -&gt; R</c>) and before <c>-&gt;</c>;</item>
/// <item>a space before <c>(</c> after <c>action</c> or <c>if</c>, or after a word that follows <c>action</c>
/// (<c>action (p: T)</c>, <c>action Place (p: T)</c>, <c>if (c)</c>). A word followed by <c>(</c> is the one
/// pair spaced two ways (<c>key(</c>, <c>initial(</c>, <c>function F(</c>, <c>invoke R.A(</c> and <c>range(</c>
/// attach), so this rule is keyed on a token TRIPLE: the writer remembers the kind before the last;</item>
/// <item>a space before <c>{</c> after a word or <c>)</c>; after <c>{</c> and before <c>}</c> next to a word
/// (<c>policy { e }</c>), with <c>{}</c> attached when empty; after <c>}</c> before a word.</item>
/// </list>
/// Raw text (<see cref="Write(DslTokenKind, string)"/> with the default kind: a fill) is a word and
/// takes part in the look-back like any token.
/// </summary>
public sealed class DslTokenWriter : ITokenWriter<DslTokenKind> {
    private readonly Func<DslTokenKind, string> _canonical;
    private readonly StringBuilder _sb = new();
    private DslTokenKind _lastKind;
    private DslTokenKind _beforeLastKind;
    private bool _lastIsWord;
    private bool _hasLast;

    public DslTokenWriter() : this(DslGrammar.CanonicalText) { }

    public DslTokenWriter(Func<DslTokenKind, string> canonical) {
        _canonical = canonical ?? throw new ArgumentNullException(nameof(canonical));
    }

    public void Write(DslTokenKind kind) => WriteToken(kind, _canonical(kind), IsWord(kind));

    public void Write(DslTokenKind kind, string text) {
        // The default kind is raw text (Grammar PrintContext.Emit(string)): word-like content.
        WriteToken(kind, text, kind == default || IsWord(kind));
    }

    public string ToText() => _sb.ToString();

    private void WriteToken(DslTokenKind kind, string text, bool isWord) {
        if (_hasLast && NeedSpace(kind, isWord))
            _sb.Append(' ');
        _sb.Append(text);
        _beforeLastKind = _lastKind;
        _lastKind = kind;
        _lastIsWord = isWord;
        _hasLast = true;
    }

    private bool NeedSpace(DslTokenKind next, bool nextIsWord) {
        if (nextIsWord && (_lastIsWord || _lastKind is DslTokenKind.Colon or DslTokenKind.Comma
                or DslTokenKind.RParen or DslTokenKind.LBrace or DslTokenKind.RBrace))
            return true;
        return next switch {
            DslTokenKind.Arrow => true,
            DslTokenKind.LParen => _lastKind is DslTokenKind.Action or DslTokenKind.If
                || (_lastIsWord && _beforeLastKind == DslTokenKind.Action),
            DslTokenKind.LBrace => _lastIsWord || _lastKind == DslTokenKind.RParen,
            DslTokenKind.RBrace => _lastIsWord,
            _ => false,
        };
    }

    private static bool IsWord(DslTokenKind kind) => kind switch {
        DslTokenKind.Colon or
        DslTokenKind.Comma or
        DslTokenKind.Dot or
        DslTokenKind.LParen or
        DslTokenKind.RParen or
        DslTokenKind.LBrace or
        DslTokenKind.RBrace or
        DslTokenKind.LBracket or
        DslTokenKind.RBracket => false,
        _ => true, // every keyword kind
    };
}
