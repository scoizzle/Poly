using System.Text;

namespace Poly.Grammar;

/// <summary>
/// A <see cref="TokenWriter{TKind}"/> backed by a <see cref="StringBuilder"/>.
/// </summary>
public class StringTokenWriter<TKind> : TokenWriter<TKind> where TKind : struct {
    private readonly StringBuilder _sb = new();

    /// <summary>Appends <paramref name="text"/> to the underlying builder.</summary>
    protected override void WriteCore(string text) => _sb.Append(text);

    /// <summary>Returns all output written so far.</summary>
    public override string GetOutput() => _sb.ToString();
}