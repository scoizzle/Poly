using Poly.Introspection;

namespace Poly.Interpretation;

/// <summary>
/// Represents a named variable in an interpretation tree that holds a reference to another value.
/// </summary>
/// <remarks>
/// Variables are named references that can be stored in scopes and retrieved by name.
/// Unlike <see cref="Parameter"/>, variables do not create their own expression nodes but delegate
/// to their underlying <see cref="Value"/>. This makes them aliases or symbolic references rather
/// than expression variables. Variables can be reassigned and support scope-based shadowing.
/// </remarks>
public class Variable(string name, Value? value = null) : Value {
    /// <summary>
    /// Gets the name of the variable.
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// Gets or sets the value this variable references.
    /// </summary>
    /// <remarks>
    /// Setting this to a new value reassigns the variable. The value can be null,
    /// but attempting to build an expression or get the type definition from an
    /// uninitialized variable will throw an exception.
    /// </remarks>
    public Value? Value { get; set; } = value;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when the variable is not initialized.</exception>
    public override ITypeDefinition GetTypeDefinition(InterpretationContext context) => Value?.GetTypeDefinition(context)
        ?? throw new InvalidOperationException($"Variable '{Name}' is not initialized.");

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when the variable is not initialized.</exception>
    public override Expression BuildExpression(InterpretationContext context) => Value?.BuildExpression(context)
        ?? throw new InvalidOperationException($"Variable '{Name}' is not initialized.");

    /// <inheritdoc />
    public override string ToString() => Name;
}