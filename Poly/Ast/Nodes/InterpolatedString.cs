namespace Poly.Ast.Nodes;

/// <summary>
/// Represents a plain interpolated string (<c>$"…{expr}…"</c>) as a first-class
/// node — the faithful shape, not a desugar to <see cref="Add"/> / String.Concat.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Literals"/> and <see cref="Holes"/> interleave exactly like the
/// source segments: <c>Literals[0] Holes[0] Literals[1] … Holes[n-1] Literals[n]</c>,
/// so <c>Literals.Count == Holes.Count + 1</c>. A literal segment may be empty
/// (e.g. <c>$"{x}"</c> has <c>["", ""]</c>).
/// </para>
/// <para>
/// Only <see cref="Holes"/> are child nodes. Literals are plain strings, not AST,
/// so analysis passes that walk <see cref="Children"/> see every embedded
/// expression and nothing else.
/// </para>
/// </remarks>
public sealed record InterpolatedString(
    IReadOnlyList<string> Literals,
    IReadOnlyList<Expression> Holes) : Expression {
    public override IEnumerable<Node?> Children => Holes;

    public override string ToString() {
        if (Literals.Count == 0)
            return "";
        var sb = new StringBuilder(Literals[0]);
        for (var i = 0; i < Holes.Count && i + 1 < Literals.Count; i++)
            sb.Append('{').Append(Holes[i]).Append('}').Append(Literals[i + 1]);
        return sb.ToString();
    }
}