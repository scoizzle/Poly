namespace Poly.Ast.Nodes;

[DebuggerDisplay("{TypeName}")]
public record TypeReference(string TypeName) : Node {
    public override string ToString() => TypeName;

};