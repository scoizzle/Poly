using Poly.Grammar;

namespace Poly.DomainModeling.Language;

/// <summary>
/// Language-owned token for the product DSL: the kind plus the half-open source range
/// <c>[Start, Start + Length)</c> into its <see cref="SourceText"/>. Scanning materialises no text: the raw
/// slice is <see cref="Span"/>, a word's text is <see cref="Name"/>, a string literal's decoded value is
/// <see cref="Unescape"/>, and <see cref="Line"/>/<see cref="Col"/> come from the source's line table.
/// Equality is the kind, the range and the <see cref="Source"/> reference.
/// </summary>
/// <remarks>
/// <see cref="Name"/> interns into <see cref="SourceText.Names"/>, which is not thread-safe, so the tokens of
/// one document are read on one thread at a time (each parse owns its <see cref="SourceText"/>).
/// </remarks>
public readonly record struct DslToken(DslTokenKind Kind, int Start, int Length, SourceText Source) : IToken<DslTokenKind> {
    /// <summary>1-based line of <see cref="Start"/>.</summary>
    public int Line => Source.GetLineColumn(Start).Line;

    /// <summary>1-based column of <see cref="Start"/>.</summary>
    public int Col => Source.GetLineColumn(Start).Col;

    /// <summary>The raw source slice: a string literal keeps its quotes and escapes; empty at end of input.</summary>
    public ReadOnlySpan<char> Span => Source.Text.Span.Slice(Start, Length);

    /// <summary>Whether the raw slice equals <paramref name="text"/> under <paramref name="comparison"/>.</summary>
    public bool TextEquals(ReadOnlySpan<char> text, StringComparison comparison = StringComparison.Ordinal) =>
        Span.Equals(text, comparison);

    /// <summary>
    /// The text of a word token: an identifier is interned once per document (<see cref="SourceText.Names"/>);
    /// a keyword is its canonical spelling, which is its source spelling. Numbers, string literals,
    /// punctuation and end of input are not names and throw, so they never enter the name table.
    /// </summary>
    public string Name() {
        if (Kind == DslTokenKind.Identifier)
            return Source.Names.Intern(Span);
        if (IsWordKind(Kind))
            return DslGrammar.CanonicalText(Kind);
        throw new InvalidOperationException($"Name() reads a word token; this token is {Kind}.");
    }

    /// <summary>
    /// The decoded value of a string literal: the quotes are dropped and <c>\"</c>/<c>\\</c> unescape; any other
    /// backslash is kept, and an unterminated literal runs to end of input. Allocates; throws for other kinds.
    /// </summary>
    public string Unescape() {
        if (Kind != DslTokenKind.StringLiteral)
            throw new InvalidOperationException($"Unescape() reads a string literal; this token is {Kind}.");

        var raw = Span;
        var sb = new StringBuilder(raw.Length);
        for (var i = 1; i < raw.Length; i++) {
            var ch = raw[i];
            if (ch == '"')
                break;
            if (ch == '\\' && i + 1 < raw.Length && raw[i + 1] is '"' or '\\') {
                sb.Append(raw[i + 1]);
                i++;
                continue;
            }
            sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Transitional decoded text: a fixed kind's canonical text, an identifier's <see cref="Name"/>, a number's
    /// slice, a string literal's <see cref="Unescape"/>, and <c>""</c> at end of input. G-13 moves its readers
    /// onto the span members and deletes it.
    /// </summary>
    [Obsolete("Read Span, Name(), Unescape() or TextEquals instead; Text is the G-12 transitional shim that G-13 deletes.", DiagnosticId = "POLYTOK001")]
    public string Text => Kind switch {
        DslTokenKind.EndOfFile => "",
        DslTokenKind.Identifier => Name(),
        DslTokenKind.Number => Span.ToString(),
        DslTokenKind.StringLiteral => Unescape(),
        _ => DslGrammar.CanonicalText(Kind),
    };

    private static bool IsWordKind(DslTokenKind kind) =>
        kind is not (DslTokenKind.EndOfFile or DslTokenKind.Identifier or DslTokenKind.Number or DslTokenKind.StringLiteral)
        && char.IsLetter(DslGrammar.CanonicalText(kind)[0]);

    // Hand-written so a default token (no Source) prints instead of throwing from Line/Col/Span.
    private bool PrintMembers(StringBuilder builder) {
        builder.Append("Kind = ").Append(Kind)
            .Append(", Start = ").Append(Start)
            .Append(", Length = ").Append(Length);
        if (Source is null || (uint)Start > (uint)Source.Length || (uint)Length > (uint)(Source.Length - Start)) {
            builder.Append(", Source = ").Append(Source is null ? "null" : "out of range");
            return true;
        }
        var (line, col) = Source.GetLineColumn(Start);
        builder.Append(", Line = ").Append(line)
            .Append(", Col = ").Append(col)
            .Append(", Span = ").Append(Span);
        return true;
    }
}