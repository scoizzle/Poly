namespace Poly.Syntax.Nodes;

[DebuggerDisplay("{TypeName}")]
public record TypeReference(string TypeName) : Node {
    public static TypeReference To<T>() => new ClrTypeReference(typeof(T));

    public override string ToString() => TypeName;
};