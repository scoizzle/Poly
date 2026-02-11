namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Represents a method invocation operation in an interpretation tree.
/// </summary>
/// <remarks>
/// Method resolution happens in semantic analysis passes (INodeAnalyzer implementations) using type information from the context.
/// Overload resolution is not yet implemented; the first matching method by name is selected.
/// </remarks>
public sealed record MethodInvocation(Node Target, string MethodName, params Node[] Arguments) : Operator {
    public override IEnumerable<Node?> Children => [Target, .. Arguments];

    public override string ToString() => $"{Target}.{MethodName}({string.Join(", ", Arguments)})";
}