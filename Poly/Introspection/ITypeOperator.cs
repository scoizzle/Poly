namespace Poly.Introspection;

/// <summary>
/// Represents an operator a type defines in the introspection system: the <see cref="OperatorKind"/> it denotes, the
/// operand types it takes and its result. A platform may evaluate an operator itself (<see cref="Method"/> is null) or
/// implement it with a callable member. Implementations should be immutable and safe for concurrent reads.
/// </summary>
public interface ITypeOperator {
    /// <summary>
    /// Gets the operator this denotes.
    /// </summary>
    OperatorKind Kind { get; }

    /// <summary>
    /// Gets the type that declares this operator.
    /// </summary>
    ITypeDefinition DeclaringTypeDefinition { get; }

    /// <summary>
    /// Gets the operands in order: one for a unary operator, left then right for a binary one.
    /// </summary>
    IEnumerable<IParameter> Parameters { get; }

    /// <summary>
    /// Gets the type of the operator's result.
    /// </summary>
    ITypeDefinition ResultTypeDefinition { get; }

    /// <summary>
    /// Gets the member that computes the operator, or null when the platform evaluates it itself.
    /// </summary>
    ITypeMethod? Method { get; }
}