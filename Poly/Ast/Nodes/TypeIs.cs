namespace Poly.Ast.Nodes;

/// <summary>
/// Represents a type test operation that checks whether a value is compatible with a target type.
/// </summary>
/// <remarks>
/// Corresponds to the <c>value is TargetType</c> operator in C#.
/// When <see cref="VariableName"/> is set, emits a declaration pattern: <c>value is TargetType name</c>.
/// The target type is specified by <see cref="TargetTypeReference"/>; semantic analysis passes resolve it to an ITypeDefinition.
/// </remarks>
public sealed record TypeIs : Expression {
    public TypeIs(Node operand, Node targetTypeReference) {
        Operand = operand ?? throw new ArgumentNullException(nameof(operand));
        TargetTypeReference = targetTypeReference ?? throw new ArgumentNullException(nameof(targetTypeReference));
    }

    public Node Operand { get; }

    public Node TargetTypeReference { get; }

    /// <summary>
    /// Optional variable name for a declaration pattern: <c>operand is TargetType VariableName</c>.
    /// </summary>
    public string? VariableName { get; init; }

    public override IEnumerable<Node?> Children => [Operand, TargetTypeReference];

    public override string ToString() => $"({Operand} is {TargetTypeReference})";


    internal static bool StaticTypeIsMatch(Type? operandType, Type targetType) {
        if (operandType is null)
            return false;
        // `is` tests the runtime type of the operand: identity, a reference upcast, or a
        // value type boxed to a base type/interface. It never widens (`int is long` is
        // false), which is why this is IsAssignableFrom and not a numeric rank.
        return targetType.IsAssignableFrom(operandType);
    }
}