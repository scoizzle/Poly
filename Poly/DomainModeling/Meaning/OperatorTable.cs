using Poly.DomainModeling.Ontology;
using Poly.Introspection;

using Add = Poly.DomainModeling.Ontology.Add;
using And = Poly.DomainModeling.Ontology.And;
using Divide = Poly.DomainModeling.Ontology.Divide;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Not = Poly.DomainModeling.Ontology.Not;
using Or = Poly.DomainModeling.Ontology.Or;
using Subtract = Poly.DomainModeling.Ontology.Subtract;

namespace Poly.DomainModeling.Meaning;

/// <summary>
/// Syntax → <see cref="OperatorKind"/>, nothing more (OQ-11). Operand and result typing
/// belong to the operator lookup on type definitions (T-03b), not here.
/// </summary>
internal static class OperatorTable {
    /// <summary>
    /// True when <paramref name="syntax"/> is an operator node. Returns false only for
    /// non-operator nodes; a malformed operator (an undefined <see cref="ComparisonKind"/>) throws.
    /// </summary>
    public static bool TryKindOf(DomainExpression syntax, out OperatorKind kind) {
        ArgumentNullException.ThrowIfNull(syntax);
        switch (syntax) {
            case Add:
                kind = OperatorKind.Add;
                return true;
            case Subtract:
                kind = OperatorKind.Subtract;
                return true;
            case Multiply:
                kind = OperatorKind.Multiply;
                return true;
            case Divide:
                kind = OperatorKind.Divide;
                return true;
            case Comparison comparison:
                kind = KindOf(comparison.Kind);
                return true;
            case And:
                kind = OperatorKind.And;
                return true;
            case Or:
                kind = OperatorKind.Or;
                return true;
            case Not:
                kind = OperatorKind.Not;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    public static OperatorKind KindOf(ComparisonKind kind) => kind switch {
        ComparisonKind.Equal => OperatorKind.Equal,
        ComparisonKind.NotEqual => OperatorKind.NotEqual,
        ComparisonKind.LessThan => OperatorKind.LessThan,
        ComparisonKind.LessThanOrEqual => OperatorKind.LessThanOrEqual,
        ComparisonKind.GreaterThan => OperatorKind.GreaterThan,
        ComparisonKind.GreaterThanOrEqual => OperatorKind.GreaterThanOrEqual,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Undefined comparison kind.")
    };
}