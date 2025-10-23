using Poly.Introspection;

namespace Poly.Interpretation;

/// <summary>
/// Represents a literal constant value in an interpretation tree.
/// </summary>
/// <remarks>
/// Literals wrap CLR objects and compile them into <see cref="System.Linq.Expressions.ConstantExpression"/> nodes.
/// The type is determined at runtime from the wrapped value's actual type.
/// </remarks>
public sealed class Literal(object? value) : Constant {
    private ITypeDefinition? _cachedTypeDefinition;
    /// <summary>
    /// Gets the wrapped constant value.
    /// </summary>
    public object? Value { get; } = value;

    /// <inheritdoc />
    public override ITypeDefinition GetTypeDefinition(InterpretationContext context) {
        if (_cachedTypeDefinition is not null)
            return _cachedTypeDefinition;

        Type type = Value?.GetType() ?? typeof(object);

        _cachedTypeDefinition = context.GetTypeDefinition(type) ??
            throw new InvalidOperationException($"Type '{type}' is not registered in the context.");

        return _cachedTypeDefinition;
    }

    /// <inheritdoc />
    public override Expression BuildExpression(InterpretationContext context) => Expression.Constant(Value);

    /// <inheritdoc />
    public override string ToString() => Value?.ToString() ?? "null";
    
    /// <summary>
    /// A predefined literal representing the boolean value <c>true</c>.
    /// </summary>
    public static readonly Value True = new Literal(true);
    
    /// <summary>
    /// A predefined literal representing the boolean value <c>false</c>.
    /// </summary>
    public static readonly Value False = new Literal(false);
}