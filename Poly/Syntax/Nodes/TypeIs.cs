using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents a type test operation that checks whether a value is compatible with a target type.
/// </summary>
/// <remarks>
/// Corresponds to the <c>value is TargetType</c> operator in C#.
/// The target type is specified by <see cref="TargetTypeReference"/>; semantic analysis passes resolve it to an ITypeDefinition.
/// </remarks>
public sealed record TypeIs : Expression {
    public TypeIs(Node operand, Node targetTypeReference) {
        Operand = operand ?? throw new ArgumentNullException(nameof(operand));
        TargetTypeReference = targetTypeReference ?? throw new ArgumentNullException(nameof(targetTypeReference));
    }

    public Node Operand { get; }

    public Node TargetTypeReference { get; }

    public override IEnumerable<Node?> Children => [Operand, TargetTypeReference];

    public override string ToString() => $"({Operand} is {TargetTypeReference})";

    /// <inheritdoc />
    public override IEnumerable<Primitives.PrimitiveNode> ToPrimitives(Primitives.ExpansionContext context) {
        foreach (var p in Operand.ToPrimitives(context)) yield return p;

        if (TargetTypeReference is not ClrTypeReference clrRef) {
            yield return new Primitives.PushConstant(0L);
            yield break;
        }

        var repr = context.Analysis.GetValueRepresentation(Operand);
        var operandMeta = context.Analysis.GetMetadata<ValueRepresentationMetadata>(Operand);
        var operandType = operandMeta?.ClrType;

        if (repr == ValueRepresentationKind.HeapRef) {
            yield return new Primitives.TypeCheck(clrRef.RuntimeType);
        }
        else if (repr is ValueRepresentationKind.StackScalar or ValueRepresentationKind.Bool) {
            var matches = StaticTypeIsMatch(operandType, clrRef.RuntimeType);
            yield return new Primitives.PushConstant(matches ? 1L : 0L);
        }
        else {
            yield return new Primitives.PushConstant(0L);
        }
    }

    internal static bool StaticTypeIsMatch(Type? operandType, Type targetType) {
        if (operandType is null)
            return false;
        if (operandType == targetType)
            return true;
        if (operandType.IsValueType && targetType == typeof(object))
            return true;
        if (!operandType.IsValueType && targetType.IsAssignableFrom(operandType))
            return true;
        if (operandType.IsValueType && targetType.IsInterface && targetType.IsAssignableFrom(operandType))
            return true;
        return false;
    }
}