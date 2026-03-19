namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Represents a method invocation operation in an interpretation tree.
/// </summary>
/// <remarks>
/// Method resolution happens in semantic analysis passes (INodeAnalyzer implementations) using type information from the context.
/// Semantic resolution selects the best matching overload using the resolved argument types.
/// </remarks>
public sealed record MethodInvocation(Node Target, string MethodName, params Node[] Arguments) : Operator {
    public override IEnumerable<Node?> Children => [Target, .. Arguments];

    public override string ToString() => $"{Target}.{MethodName}({string.Join(", ", Arguments)})";
}