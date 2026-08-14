namespace Poly.Grammar;

/// <summary>
/// Prints patterns as canonical text — the recognition table's emit surface.
///
/// Fixed <see cref="MatchKind{TToken,TTokenKind}"/> elements emit the language's
/// canonical text for that kind (supplied by the caller). Content-bearing positions
/// — <see cref="Value{TToken,TTokenKind}"/>, <see cref="MatchPredicate{TToken,TTokenKind}"/>,
/// <see cref="Any{TToken,TTokenKind}"/>, and the bodies of <see cref="Optional{TToken,TTokenKind}"/> /
/// <see cref="Repeat{TToken,TTokenKind}"/> / <see cref="Ref{TToken,TTokenKind}"/> /
/// <see cref="LeftAssoc{TToken,TTokenKind}"/> / <see cref="Balanced{TToken,TTokenKind}"/> —
/// delegate to a handler callback so the caller supplies runtime values. Without a
/// callback those positions emit nothing (the pattern prints as its fixed skeleton).
///
/// The printer emits **tokens** into an <see cref="ITokenWriter{TTokenKind}"/>, never
/// spaces: separators are the writer's job (raw writers append verbatim; language
/// writers insert the spaces their reader discards).
/// </summary>
public sealed class Printer<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct {
    private readonly Grammar<TToken, TTokenKind> _grammar;
    private readonly Func<ITokenWriter<TTokenKind>> _writerFactory;

    /// <param name="grammar">The grammar whose patterns this printer walks.</param>
    /// <param name="canonical">Kind → canonical text (the tokenizer's inverse for fixed tokens).</param>
    public Printer(Grammar<TToken, TTokenKind> grammar, Func<TTokenKind, string> canonical)
        : this(grammar, canonical, () => new StringTokenWriter<TTokenKind>(canonical)) {
    }

    /// <param name="grammar">The grammar whose patterns this printer walks.</param>
    /// <param name="canonical">Kind → canonical text (the tokenizer's inverse for fixed tokens).</param>
    /// <param name="writerFactory">Creates a fresh output writer per print (separators are writer-owned).</param>
    public Printer(
        Grammar<TToken, TTokenKind> grammar,
        Func<TTokenKind, string> canonical,
        Func<ITokenWriter<TTokenKind>> writerFactory) {
        _grammar = grammar ?? throw new ArgumentNullException(nameof(grammar));
        _ = canonical ?? throw new ArgumentNullException(nameof(canonical));
        _writerFactory = writerFactory ?? throw new ArgumentNullException(nameof(writerFactory));
    }

    /// <summary>Prints a pattern by name. Throws if the rule or pattern is unknown (fail closed).</summary>
    public string Print(string ruleName, string patternName, Action<PrintContext<TToken, TTokenKind>>? onContent = null) {
        var pattern = _grammar.GetPatterns(ruleName).FirstOrDefault(p => p.Name == patternName)
            ?? throw new ArgumentException($"Unknown pattern '{patternName}' in rule '{ruleName}'");
        return Print(pattern, onContent);
    }

    /// <summary>Prints a pattern. Content positions delegate to <paramref name="onContent"/> when provided.</summary>
    public string Print(Pattern<TToken, TTokenKind> pattern, Action<PrintContext<TToken, TTokenKind>>? onContent = null) {
        var writer = _writerFactory();
        PrintInto(writer, pattern, onContent);
        return writer.ToText();
    }

    /// <summary>
    /// Prints a named pattern into <paramref name="writer"/> using a FRESH scratch writer
    /// (nested prints must never disturb the caller's in-progress output).
    /// </summary>
    internal void PrintInto(ITokenWriter<TTokenKind> writer, string ruleName, string patternName, Action<PrintContext<TToken, TTokenKind>>? onContent) {
        var pattern = _grammar.GetPatterns(ruleName).FirstOrDefault(p => p.Name == patternName)
            ?? throw new ArgumentException($"Unknown pattern '{patternName}' in rule '{ruleName}'");
        var scratch = _writerFactory();
        PrintInto(scratch, pattern, onContent);
        writer.Write(default(TTokenKind), scratch.ToText());
    }

    private void PrintInto(ITokenWriter<TTokenKind> writer, Pattern<TToken, TTokenKind> pattern, Action<PrintContext<TToken, TTokenKind>>? onContent) {
        var ctx = new PrintContext<TToken, TTokenKind>(this, writer);
        foreach (var element in pattern.Elements)
            PrintElement(element, ctx, onContent);
    }

    private void PrintElement(IPatternElement<TToken, TTokenKind> element, PrintContext<TToken, TTokenKind> ctx, Action<PrintContext<TToken, TTokenKind>>? onContent) {
        switch (element) {
            case MatchKind<TToken, TTokenKind> k:
                ctx.Emit(k.Kind);
                return;

            case Value<TToken, TTokenKind>:
            case MatchPredicate<TToken, TTokenKind>:
            case Any<TToken, TTokenKind>:
                onContent?.Invoke(ctx);
                return;

            case Optional<TToken, TTokenKind>:
            case Repeat<TToken, TTokenKind>:
            case Ref<TToken, TTokenKind>:
            case LeftAssoc<TToken, TTokenKind>:
                // Handler decides whether/how the inner content appears (may emit nothing).
                onContent?.Invoke(ctx);
                return;

            case Balanced<TToken, TTokenKind> b:
                ctx.Emit(b.Open);
                onContent?.Invoke(ctx);
                ctx.Emit(b.Close);
                return;

            default:
                throw new InvalidOperationException($"Unknown pattern element '{element.GetType().Name}'");
        }
    }
}

/// <summary>
/// Handler surface for content-bearing print positions. Emit fixed kinds or raw text;
/// print nested rules into the same output via <see cref="PrintRule"/>. Binders never
/// emit spaces — the underlying writer owns separators.
/// </summary>
public sealed class PrintContext<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct {
    private readonly Printer<TToken, TTokenKind> _printer;
    private readonly ITokenWriter<TTokenKind> _writer;

    internal PrintContext(Printer<TToken, TTokenKind> printer, ITokenWriter<TTokenKind> writer) {
        _printer = printer;
        _writer = writer;
    }

    /// <summary>Emits the canonical text for a kind.</summary>
    public void Emit(TTokenKind kind) => _writer.Write(kind);

    /// <summary>Emits raw text (e.g. a runtime value at a Value position).</summary>
    public void Emit(string text) => _writer.Write(default(TTokenKind), text);

    /// <summary>Prints a named pattern of a rule into the current output.</summary>
    public void PrintRule(string ruleName, string patternName) =>
        _printer.PrintInto(_writer, ruleName, patternName, onContent: null);

    /// <summary>Prints a named pattern with a content callback (nested value positions).</summary>
    public void PrintRule(string ruleName, string patternName, Action<PrintContext<TToken, TTokenKind>> onContent) =>
        _printer.PrintInto(_writer, ruleName, patternName, onContent);
}