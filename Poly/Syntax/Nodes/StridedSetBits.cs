namespace Poly.Syntax.Nodes;

/// <summary>
/// Sets bits in a bit-packed <c>long[]</c> array over a strided range.
/// Equivalent to: <c>for (j = start; j &lt;= limit; j += step) arr[j&gt;&gt;6] |= 1L &lt;&lt; (j&amp;63)</c>
/// The entire loop executes as a single compiled expression — no per-iteration µop dispatch.
/// </summary>
public sealed record StridedSetBits(Node Array, Node StartValue, Node Step, Node Limit) : Statement {
    public override IEnumerable<Node?> Children => [Array, StartValue, Step, Limit];
    public override string ToString() => $"StridedSetBits({Array}, {StartValue}, {Step}, {Limit})";

    /// <inheritdoc />
    public override IEnumerable<Primitives.PrimitiveNode> ToPrimitives(Primitives.ExpansionContext context) {
        foreach (var p in Array.ToPrimitives(context)) yield return p;
        foreach (var p in StartValue.ToPrimitives(context)) yield return p;
        foreach (var p in Step.ToPrimitives(context)) yield return p;
        foreach (var p in Limit.ToPrimitives(context)) yield return p;
        yield return new Primitives.StridedSet();
    }
}