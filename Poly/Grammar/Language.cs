namespace Poly.Grammar;

/// <summary>
/// An immutable recognition table plus the printer that emits it. Matcher and
/// printer share one <see cref="Grammar{TToken,TTokenKind}"/> — the parse/print
/// cycle a domain session holds after loading the libraries the domain declared.
/// </summary>
public sealed class Language<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct {
    public Grammar<TToken, TTokenKind> Grammar { get; }

    public Func<TTokenKind, string> Canonical { get; }

    public Func<ITokenWriter<TTokenKind>> WriterFactory { get; }

    public Printer<TToken, TTokenKind> Printer { get; }

    public Language(
        Grammar<TToken, TTokenKind> grammar,
        Func<TTokenKind, string> canonical,
        Func<ITokenWriter<TTokenKind>>? writerFactory = null) {
        ArgumentNullException.ThrowIfNull(grammar);
        ArgumentNullException.ThrowIfNull(canonical);
        Grammar = grammar;
        Canonical = canonical;
        WriterFactory = writerFactory ?? (() => new StringTokenWriter<TTokenKind>(canonical));
        Printer = new Printer<TToken, TTokenKind>(Grammar, Canonical, WriterFactory);
    }

    /// <summary>This language plus <paramref name="contribute"/>. This instance is unchanged.</summary>
    public Language<TToken, TTokenKind> Extend(Action<GrammarBuilder<TToken, TTokenKind>> contribute) {
        ArgumentNullException.ThrowIfNull(contribute);
        return new Language<TToken, TTokenKind>(Grammar.Extend(contribute), Canonical, WriterFactory);
    }

    public Matcher<TToken, TTokenKind> Matcher(ITokenStreamReader<TToken, TTokenKind> reader) =>
        new(Grammar, reader);
}