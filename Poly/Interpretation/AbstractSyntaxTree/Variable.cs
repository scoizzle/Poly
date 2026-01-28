namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Represents a named variable in an interpretation tree that holds a reference to another value.
/// </summary>
/// <remarks>
/// Variables are named references that can be stored in scopes and retrieved by name.
/// Unlike <see cref="Parameter"/>, variables delegate to their underlying <see cref="Node"/>.
/// This makes them aliases or symbolic references rather than expression variables.
/// Variables support scope-based shadowing.
/// Type information is resolved by semantic analysis passes (INodeAnalyzer implementations).
/// </remarks>
public sealed record Variable(string Name, Node? Value = null) : Node {
    /// <summary>
    /// Gets or sets the value this variable references.
    /// </summary>
    /// <remarks>
    /// The value can be null, but attempting to build an expression or get the type definition
    /// from an uninitialized variable may throw an exception.
    /// </remarks>
    public Node? Value { get; set; } = Value;

    public override IEnumerable<Node?> Children => [Value];

    /// <inheritdoc />
    public override string ToString() => Name;
}