namespace Poly.Syntax.Nodes;

[DebuggerDisplay("{TypeName}")]
public record TypeReference(string TypeName) : Node {
    public static TypeReference To<T>() => new(typeof(T).FullName!);

    public override string ToString() => TypeName;
};