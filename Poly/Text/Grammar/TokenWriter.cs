namespace Poly.Text.Grammar;

/// <summary>
/// Abstract base for output formatting. Owns all formatting policy:
/// canonical text per <typeparamref name="TKind"/>, spacing, indentation,
/// and separator insertion. Subclasses decide the output destination
/// (StringBuilder, stream, etc.).
/// </summary>
public abstract class TokenWriter<TKind> where TKind : struct {
    private int _indentLevel;
    private int _indentSize = 2;

    /// <summary>Number of spaces per indent level.</summary>
    public int IndentSize {
        get => _indentSize;
        set => _indentSize = Math.Max(0, value);
    }

    /// <summary>Current indent level (0-based).</summary>
    public int IndentLevel => _indentLevel;

    // ═════════════════════════════════════════════════════════
    //  Subclass contract
    // ═════════════════════════════════════════════════════════

    /// <summary>Appends raw text to the output. Subclass override.</summary>
    protected abstract void WriteCore(string text);

    /// <summary>Returns all written output. Subclass override.</summary>
    public abstract string GetOutput();

    // ═════════════════════════════════════════════════════════
    //  Canonical text
    // ═════════════════════════════════════════════════════════

    /// <summary>
    /// Maps a token kind to its canonical output text.
    /// Override to customise per-grammar formatting.
    /// Default: lower-cased enum name.
    /// </summary>
    public virtual string CanonicalText(TKind kind) => kind.ToString()!.ToLowerInvariant();

    // ═════════════════════════════════════════════════════════
    //  High-level emit
    // ═════════════════════════════════════════════════════════

    /// <summary>Emits the canonical text for a token kind.</summary>
    public void Write(TKind kind) => WriteCore(CanonicalText(kind));

    /// <summary>
    /// Emits a value-bearing token (e.g. String, Number, Identifier)
    /// with its runtime text. The default writes the raw value as-is;
    /// subclass overrides can add delimiters (quotes, etc.).
    /// </summary>
    public virtual void WriteValue(TKind kind, string value) => WriteCore(value);

    /// <summary>Emits raw text as-is.</summary>
    public void WriteRaw(string text) => WriteCore(text);

    /// <summary>Emits a space.</summary>
    public void Space() => WriteCore(" ");

    /// <summary>Emits a newline followed by the current indent.</summary>
    public void Newline() {
        WriteCore("\n");
        for (int i = 0; i < _indentLevel * _indentSize; i++)
            WriteCore(" ");
    }

    /// <summary>Emits a separator token followed by a space.</summary>
    public void Separator(TKind kind) {
        Write(kind);
        Space();
    }

    // ═════════════════════════════════════════════════════════
    //  Indentation
    // ═════════════════════════════════════════════════════════

    /// <summary>Increases the indent level by one.</summary>
    public void PushIndent() => _indentLevel++;

    /// <summary>Decreases the indent level by one.</summary>
    public void PopIndent() {
        if (_indentLevel > 0) _indentLevel--;
    }

    /// <summary>
    /// Resets indent to zero. Useful before starting a new top-level print.
    /// </summary>
    public void ResetIndent() => _indentLevel = 0;
}