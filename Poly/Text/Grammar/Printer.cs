namespace Poly.Text.Grammar;

/// <summary>
/// Walks a pattern's elements and produces formatted output via a
/// <see cref="TokenWriter{TKind}"/>. Fixed tokens (e.g. <see cref="MatchToken{TKind}"/>)
/// are emitted automatically. Content-bearing positions — inside a
/// <see cref="Balanced{TKind}"/> body, for each <see cref="ManyOf{TKind}"/> item,
/// or at a <see cref="MatchPredicate{TKind}"/> / <see cref="AnyToken{TKind}"/> —
/// are delegated to an optional content callback.
///
/// <code>
/// var writer = new StringTokenWriter&lt;JsonKind&gt;();
/// var printer = new Printer&lt;JsonKind&gt;(jsonGrammar, writer);
/// printer.Print("object", ctx =&gt; {
///     ctx.Emit(JsonKind.String, "name");
///     ctx.Emit(JsonKind.Colon);
///     ctx.Emit(JsonKind.Number, "42");
/// });
/// var json = writer.GetOutput(); // {"name":42}
/// </code>
/// </summary>
public sealed class Printer<TKind> where TKind : struct {
    private readonly Grammar<TKind> _grammar;
    private readonly TokenWriter<TKind> _writer;

    public Printer(Grammar<TKind> grammar, TokenWriter<TKind> writer) {
        _grammar = grammar ?? throw new ArgumentNullException(nameof(grammar));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    /// <summary>The underlying writer, for direct use in content callbacks.</summary>
    public TokenWriter<TKind> Writer => _writer;

    /// <summary>
    /// Prints the named pattern by walking its elements. Fixed tokens are
    /// emitted automatically. At content-bearing positions (Balanced body,
    /// ManyOf items, Predicate/AnyToken value), <paramref name="onContent"/>
    /// is called with a <see cref="PrintContext{TKind}"/> to supply runtime values.
    ///
    /// If <paramref name="onContent"/> is <c>null</c>, the pattern must consist
    /// entirely of fixed tokens or the output will be incomplete.
    /// </summary>
    public void Print(string patternName, Action<PrintContext<TKind>>? onContent = null) {
        var pattern = FindPattern(patternName);
        if (pattern == null)
            throw new ArgumentException($"No pattern named '{patternName}' found in any rule.", nameof(patternName));

        var ctx = new PrintContext<TKind>(this, _writer, _grammar);
        WalkElements(pattern.Elements, ctx, onContent);
    }

    // ═════════════════════════════════════════════════════════
    //  Internal
    // ═════════════════════════════════════════════════════════

    private Pattern<TKind>? FindPattern(string name) {
        foreach (var ruleName in _grammar.KnownRules) {
            foreach (var p in _grammar.GetPatterns(ruleName)) {
                if (p.Name == name) return p;
            }
        }
        return null;
    }

    internal void WalkElements(
        IReadOnlyList<IPatternElement<TKind>> elements,
        PrintContext<TKind> ctx,
        Action<PrintContext<TKind>>? onContent) {
        foreach (var element in elements) {
            switch (element) {
                case MatchToken<TKind> mt:
                    _writer.Write(mt.Kind);
                    break;

                case MatchValue<TKind>:
                case MatchPredicate<TKind>:
                    InvokeContent(ctx, onContent);
                    break;

                case Optional<TKind>:
                    // Try content — if callback emits nothing, skip.
                    InvokeContent(ctx, onContent);
                    break;

                case ManyOf<TKind>:
                    InvokeContent(ctx, onContent);
                    break;

                case Balanced<TKind> bal:
                    _writer.Write(bal.Open);
                    if (onContent != null) {
                        _writer.PushIndent();
                        _writer.Newline();
                        InvokeContent(ctx, onContent);
                        _writer.Newline();
                        _writer.PopIndent();
                    }
                    _writer.Write(bal.Close);
                    break;

                case AnyToken<TKind>:
                    InvokeContent(ctx, onContent);
                    break;
            }
        }
    }

    private static void InvokeContent(
        PrintContext<TKind> ctx,
        Action<PrintContext<TKind>>? onContent) {
        onContent?.Invoke(ctx);
    }
}

/// <summary>
/// Context passed to content callbacks during printing.
/// Provides methods to emit tokens, write raw text, and sub-print patterns.
/// </summary>
public readonly ref struct PrintContext<TKind> where TKind : struct {
    private readonly Printer<TKind> _printer;
    private readonly TokenWriter<TKind> _writer;

    internal PrintContext(Printer<TKind> printer, TokenWriter<TKind> writer, Grammar<TKind> grammar) {
        _printer = printer;
        _writer = writer;
    }

    /// <summary>Emits the canonical text for a token kind.</summary>
    public void Emit(TKind kind) => _writer.Write(kind);

    /// <summary>
    /// Emits a value-bearing token (e.g. String, Number, Identifier)
    /// with its runtime text.
    /// </summary>
    public void Emit(TKind kind, string value) => _writer.WriteValue(kind, value);

    /// <summary>Emits raw text as-is (no canonical mapping).</summary>
    public void EmitRaw(string text) => _writer.WriteRaw(text);

    /// <summary>Writes a space.</summary>
    public void Space() => _writer.Space();

    /// <summary>Writes a newline + indent.</summary>
    public void Newline() => _writer.Newline();
}