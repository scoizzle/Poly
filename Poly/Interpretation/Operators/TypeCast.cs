using Poly.Introspection;

namespace Poly.Interpretation.Operators;

/// <summary>
/// Represents an explicit type cast operation that converts a value to a specified type.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expression.Convert(Expression, Type)"/> which performs an explicit type conversion.
/// Corresponds to the <c>(TargetType)value</c> cast operator in C#.
/// For checked conversions that throw on overflow, use <see cref="Expression.ConvertChecked"/>.
/// </remarks>
public sealed class TypeCast(Value operand, ITypeDefinition targetType, bool isChecked = false) : Operator {
    /// <summary>
    /// Gets the value to cast.
    /// </summary>
    public Value Operand { get; } = operand ?? throw new ArgumentNullException(nameof(operand));
    
    /// <summary>
    /// Gets the target type to cast to.
    /// </summary>
    public ITypeDefinition TargetType { get; } = targetType ?? throw new ArgumentNullException(nameof(targetType));
    
    /// <summary>
    /// Gets whether to use checked conversion (throws on overflow).
    /// </summary>
    public bool IsChecked { get; } = isChecked;

    /// <inheritdoc />
    public override ITypeDefinition GetTypeDefinition(InterpretationContext context) {
        return TargetType;
    }

    /// <inheritdoc />
    public override Expression BuildExpression(InterpretationContext context) {
        Expression operandExpr = Operand.BuildExpression(context);
        Type targetClrType = TargetType.ReflectedType;
        
        return IsChecked 
            ? Expression.ConvertChecked(operandExpr, targetClrType)
            : Expression.Convert(operandExpr, targetClrType);
    }

    /// <inheritdoc />
    public override string ToString() => $"(({TargetType.Name}){Operand})";
}
