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
    public string Print(
        string ruleName,
        string patternName,
        Action<PrintContext<TToken, TTokenKind>>? onContent = null,
        IReadOnlyDictionary<string, string>? fills = null) =>
        Print(_grammar.GetPattern(ruleName, patternName), onContent, fills);

    /// <summary>
    /// Prints a row whose content positions are filled, in element order, from <paramref name="content"/>
    /// (a null or empty value is an empty slot). Named holes take no value here.
    /// </summary>
    public string Render(string rule, string pattern, params string?[] content) =>
        Render(rule, pattern, Fills.None, content);

    /// <summary>
    /// Prints a row: named holes from <paramref name="fills"/>, content positions in element order from
    /// <paramref name="content"/> (a null or empty value is an empty slot). The count is strict: when the
    /// row has more or fewer content positions than values were supplied, the print is first reported to
    /// <see cref="PrintTrace"/> (supplied and consumed), then this throws naming the row (fail closed).
    /// </summary>
    public string Render(string rule, string pattern, IReadOnlyDictionary<string, string> fills, params string?[] content) {
        ArgumentNullException.ThrowIfNull(fills);
        content ??= [];
        var consumed = 0;
        var text = PrintTop(
            _grammar.GetPattern(rule, pattern),
            ctx => {
                var i = consumed++;
                if (i < content.Length && content[i] is { Length: > 0 } piece)
                    ctx.Emit(piece);
            },
            fills,
            content.Length);
        if (consumed != content.Length)
            throw new InvalidOperationException(
                $"Pattern '{rule}/{pattern}' has {consumed} content positions; the caller supplied {content.Length}.");
        return text;
    }

    /// <summary>Prints a pattern. Named holes use <paramref name="fills"/>; unnamed holes call <paramref name="onContent"/>.</summary>
    public string Print(
        Pattern<TToken, TTokenKind> pattern,
        Action<PrintContext<TToken, TTokenKind>>? onContent = null,
        IReadOnlyDictionary<string, string>? fills = null) =>
        PrintTop(pattern, onContent, fills, suppliedContentCount: null);

    /// <summary>
    /// A top-level print. Under an active <see cref="PrintTrace"/> scope it also records an
    /// observation; nested prints (<see cref="PrintContext{TToken,TTokenKind}.PrintRule(string,string)"/>)
    /// never do. The printed text is the same either way.
    /// </summary>
    private string PrintTop(
        Pattern<TToken, TTokenKind> pattern,
        Action<PrintContext<TToken, TTokenKind>>? onContent,
        IReadOnlyDictionary<string, string>? fills,
        int? suppliedContentCount) {
        if (!PrintTrace.IsActive) {
            var writer = _writerFactory();
            PrintInto(writer, pattern, onContent, fills);
            return writer.ToText();
        }

        var counting = new CountingTokenWriter(_writerFactory());
        var emitted = new List<bool>();
        PrintInto(counting, pattern, ctx => {
            var before = counting.Writes;
            onContent?.Invoke(ctx);
            emitted.Add(counting.Writes > before);
        }, fills);
        var text = counting.ToText();
        PrintTrace.Record(new PrintObservation(
            _grammar,
            RuleOf(pattern),
            pattern.Name,
            fills is null ? [] : [.. fills.Keys.Order(StringComparer.Ordinal)],
            emitted,
            suppliedContentCount,
            text));
        return text;
    }

    /// <summary>The rule that owns <paramref name="pattern"/> in this printer's table; null when none does.</summary>
    private string? RuleOf(Pattern<TToken, TTokenKind> pattern) {
        foreach (var (rule, patterns) in _grammar.Rules) {
            foreach (var candidate in patterns) {
                if (ReferenceEquals(candidate, pattern))
                    return rule;
            }
        }
        return null;
    }

    /// <summary>Passes every write through and counts the writes that carry text (observation only).</summary>
    private sealed class CountingTokenWriter(ITokenWriter<TTokenKind> inner) : ITokenWriter<TTokenKind> {
        public int Writes { get; private set; }

        public void Write(TTokenKind kind) {
            Writes++;
            inner.Write(kind);
        }

        public void Write(TTokenKind kind, string text) {
            if (!string.IsNullOrEmpty(text))
                Writes++;
            inner.Write(kind, text);
        }

        public string ToText() => inner.ToText();
    }

    /// <summary>
    /// Prints a named pattern into <paramref name="writer"/> using a FRESH scratch writer
    /// (nested prints must never disturb the caller's in-progress output).
    /// </summary>
    internal void PrintInto(
        ITokenWriter<TTokenKind> writer,
        string ruleName,
        string patternName,
        Action<PrintContext<TToken, TTokenKind>>? onContent,
        IReadOnlyDictionary<string, string>? fills = null) {
        var pattern = _grammar.GetPattern(ruleName, patternName);
        var scratch = _writerFactory();
        PrintInto(scratch, pattern, onContent, fills);
        writer.Write(default(TTokenKind), scratch.ToText());
    }

    private void PrintInto(
        ITokenWriter<TTokenKind> writer,
        Pattern<TToken, TTokenKind> pattern,
        Action<PrintContext<TToken, TTokenKind>>? onContent,
        IReadOnlyDictionary<string, string>? fills) {
        var ctx = new PrintContext<TToken, TTokenKind>(this, writer, fills);
        foreach (var element in pattern.Elements)
            PrintElement(element, ctx, onContent);
    }

    private void PrintElement(IPatternElement<TToken, TTokenKind> element, PrintContext<TToken, TTokenKind> ctx, Action<PrintContext<TToken, TTokenKind>>? onContent) {
        switch (element) {
            case MatchKind<TToken, TTokenKind> k:
                ctx.Emit(k.Kind);
                return;

            case Value<TToken, TTokenKind> { Name: { Length: > 0 } name }:
                ctx.EmitFill(name);
                return;

            case Slot<TToken, TTokenKind> slot:
                ctx.EmitFill(slot.Name);
                return;

            case Word<TToken, TTokenKind> w:
                ctx.Emit(w.Kind, w.Text);
                return;

            case MatchPredicate<TToken, TTokenKind> { Label: { Length: > 0 } label } when ctx.HasFill(label):
                ctx.EmitFill(label);
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

            case NotFollowedBy<TToken, TTokenKind>:
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
    private readonly IReadOnlyDictionary<string, string> _fills;

    internal PrintContext(
        Printer<TToken, TTokenKind> printer,
        ITokenWriter<TTokenKind> writer,
        IReadOnlyDictionary<string, string>? fills = null) {
        _printer = printer;
        _writer = writer;
        _fills = fills ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>Emits the canonical text for a kind.</summary>
    public void Emit(TTokenKind kind) => _writer.Write(kind);

    /// <summary>Emits raw text (e.g. a runtime value at a Value position).</summary>
    public void Emit(string text) => _writer.Write(default(TTokenKind), text);

    /// <summary>Emits <paramref name="text"/> as a token of <paramref name="kind"/> (a row's own spelling, such as a Word).</summary>
    internal void Emit(TTokenKind kind, string text) => _writer.Write(kind, text);

    /// <summary>True when a named fill was supplied for this print.</summary>
    public bool HasFill(string name) =>
        name is { Length: > 0 } && _fills.ContainsKey(name);

    /// <summary>Emits the named fill. Missing name fails closed.</summary>
    public void EmitFill(string name) {
        if (!_fills.TryGetValue(name, out var text))
            throw new InvalidOperationException($"No print fill for '{name}'.");
        Emit(text);
    }

    /// <summary>Prints a named pattern of a rule into the current output.</summary>
    public void PrintRule(string ruleName, string patternName) =>
        _printer.PrintInto(_writer, ruleName, patternName, onContent: null);

    /// <summary>Prints a named pattern with a content callback (nested value positions).</summary>
    public void PrintRule(string ruleName, string patternName, Action<PrintContext<TToken, TTokenKind>> onContent) =>
        _printer.PrintInto(_writer, ruleName, patternName, onContent);
}

/// <summary>Named print fills for <see cref="Printer{TToken,TTokenKind}.Render(string, string, IReadOnlyDictionary{string, string}, string?[])"/>.</summary>
public static class Fills {
    /// <summary>No named fills.</summary>
    public static IReadOnlyDictionary<string, string> None { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The fills named by <paramref name="entries"/>. A name given twice throws (fail closed).</summary>
    public static IReadOnlyDictionary<string, string> Of(params (string Name, string Value)[] entries) {
        ArgumentNullException.ThrowIfNull(entries);
        var fills = new Dictionary<string, string>(entries.Length, StringComparer.Ordinal);
        foreach (var (name, value) in entries) {
            if (!fills.TryAdd(name, value))
                throw new ArgumentException($"The fill '{name}' is named twice.", nameof(entries));
        }
        return fills;
    }
}