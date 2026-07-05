namespace Poly.Syntax.Nodes;

[DebuggerDisplay("{TypeName}")]
public record TypeReference(string TypeName) : Node {
    public static TypeReference To<T>() => new ClrTypeReference(typeof(T));

    public override string ToString() => TypeName;

    /// <inheritdoc />
    public override IEnumerable<Primitives.PrimitiveNode> ToPrimitives(Primitives.ExpansionContext context) {
        // Type references are compile-time metadata only
        yield return new Primitives.PushConstant(0L);
    }
};