namespace Poly.Syntax.AbstractSyntaxTree;

/// <summary>
/// Represents instance creation by selecting and invoking a constructor on a type.
/// </summary>
/// <remarks>
/// The <see cref="Type"/> is structural and is typically a <see cref="TypeReference"/> or
/// <see cref="TypeDefinitionReference"/>. Constructor resolution happens in semantic analysis
/// passes using the resolved argument and target type information.
/// </remarks>
/// <param name="Type">The type being instantiated.</param>
/// <param name="Arguments">The constructor arguments.</param>
public sealed record New(Node Type, params Node[] Arguments) : Operator {
    public override IEnumerable<Node?> Children => [Type, .. Arguments];

    public override string ToString() => $"new {Type}({string.Join(", ", Arguments)})";
}