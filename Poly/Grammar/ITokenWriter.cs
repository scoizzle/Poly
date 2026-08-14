namespace Poly.Grammar;

/// <summary>
/// Receives the tokens a <see cref="Printer{TToken,TTokenKind}"/> emits. Writers own
/// separators: the inverse of a reader's whitespace/comment skip. A raw writer appends
/// canonical text verbatim; a language writer inserts the spaces its reader discards.
/// </summary>
public interface ITokenWriter<TTokenKind> where TTokenKind : struct {
    /// <summary>Emits the canonical text for a kind (value-less fixed token).</summary>
    void Write(TTokenKind kind);

    /// <summary>Emits the value text for a content-bearing kind (e.g. an identifier).</summary>
    void Write(TTokenKind kind, string text);

    /// <summary>Returns the accumulated output.</summary>
    string ToText();
}