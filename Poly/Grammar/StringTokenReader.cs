namespace Poly.Grammar;

/// <summary>
/// Base class for string-backed token readers. Provides character-level
/// navigation (<see cref="PeekChar"/>, <see cref="AdvanceChar"/>), line/column
/// tracking, and whitespace skipping for use by concrete scanner implementations.
/// </summary>
public abstract class StringTokenReader<TKind> : CharSourceTokenReader<TKind> where TKind : struct {
    /// <summary>The full source text being scanned.</summary>
    public string Text { get; }

    protected StringTokenReader(string text) : base(new StringCharSource(text)) {
        Text = text ?? throw new ArgumentNullException(nameof(text));
    }
}