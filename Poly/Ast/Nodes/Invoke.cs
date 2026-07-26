using Poly.Interpretation.Analysis.Semantics;

namespace Poly.Ast.Nodes;

/// <summary>
/// Represents a method invocation operation in an interpretation tree.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Delegate"/> is a structural reference to the method being called — typically a
/// <see cref="Member"/> node (e.g. <c>target.MethodName</c>) resolved by
/// semantic analysis passes.
/// </para>
/// <para>
/// Using a node as the method reference (rather than a bare string) makes call sites
/// structurally consistent with all other node references, allows the same resolution
/// infrastructure to handle both property reads and method calls, and makes the intent
/// explicit in the tree.
/// </para>
/// </remarks>
/// <param name="Delegate">The method reference node, typically a <see cref="Member"/>.</param>
/// <param name="Arguments">The arguments to pass to the method.</param>
public sealed record Invoke(Node Delegate, params Node[] Arguments) : Expression {
    /// <summary>
    /// Optional generic type arguments for the invocation, e.g. <c>&lt;Book&gt;</c> in <c>Set&lt;Book&gt;()</c>.
    /// </summary>
    public IReadOnlyList<Node> TypeArguments { get; init; } = [];

    public override IEnumerable<Node?> Children => [Delegate, .. TypeArguments, .. Arguments];

    public override string ToString() {
        var typeArgs = TypeArguments.Count > 0 ? $"<{string.Join(", ", TypeArguments)}>" : "";
        return $"{Delegate}{typeArgs}({string.Join(", ", Arguments)})";
    }
}