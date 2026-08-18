namespace Poly.Grammar;

/// <summary>
/// Raw token writer: appends <c>canonical(kind)</c> / <c>text</c> verbatim with no
/// inserted separators — the engine's skeleton surface (a Printer's default).
/// </summary>
public sealed class StringTokenWriter<TTokenKind> : ITokenWriter<TTokenKind>
    where TTokenKind : struct {
    private readonly Func<TTokenKind, string> _canonical;
    private readonly StringBuilder _sb = new();

    public StringTokenWriter(Func<TTokenKind, string> canonical) {
        _canonical = canonical ?? throw new ArgumentNullException(nameof(canonical));
    }

    public void Write(TTokenKind kind) => _sb.Append(_canonical(kind));

    public void Write(TTokenKind kind, string text) => _sb.Append(text);

    public string ToText() => _sb.ToString();
}