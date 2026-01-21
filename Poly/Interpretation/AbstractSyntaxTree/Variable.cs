namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Represents a named variable in an interpretation tree that holds a reference to another value.
/// </summary>
/// <remarks>
/// Variables are named references that can be stored in scopes and retrieved by name.
/// Unlike <see cref="Parameter"/>, variables do not create their own expression nodes but delegate
/// to their underlying <see cref="Node"/>. This makes them aliases or symbolic references rather
/// than expression variables. Variables can be reassigned and support scope-based shadowing.
/// Type information is resolved by semantic analysis middleware.
/// </remarks>
public sealed record Variable(string Name, Node? Value = null) : Node
{
    /// <summary>
    /// Gets or sets the value this variable references.
    /// </summary>
    /// <remarks>
    /// Setting this to a new value reassigns the variable. The value can be null,
    /// but attempting to build an expression or get the type definition from an
    /// uninitialized variable will throw an exception.
    /// </remarks>
    public Node? Value { get; set; } = Value;

    /// <inheritdoc />
    public override string ToString() => Name;
}