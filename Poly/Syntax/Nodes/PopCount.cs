namespace Poly.Syntax.Nodes;

/// <summary>
/// Count set bits (popcount) in a <c>long</c> value using hardware POPCNT.
/// Compiles to <c>System.Numerics.BitOperations.PopCount(ulong)</c>.
/// </summary>
public sealed record PopCount(Node Operand) : Expression {
    public override IEnumerable<Node?> Children => [Operand];
    public override string ToString() => $"CountBits({Operand})";

    /// <inheritdoc />
    public override IEnumerable<Primitives.PrimitiveNode> ToPrimitives(Primitives.ExpansionContext context) {
        foreach (var p in Operand.ToPrimitives(context)) yield return p;
        yield return new Primitives.CountBits();
    }
}