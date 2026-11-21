using Poly.DomainModeling.Libraries.Temporal;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation.CSharp;

namespace Poly.DomainModeling.Lowering;

// Temporal operands of an ordering comparison: a DateTime value is compared as its UTC instant (#569).

public sealed partial class DomainExpressionLoweringPass {
    /// <summary>The comparisons that order values: <c>&lt;</c>, <c>&lt;=</c>, <c>&gt;</c>, <c>&gt;=</c>.</summary>
    private static bool IsInstantOrdering(ComparisonKind kind) =>
        kind is ComparisonKind.LessThan or ComparisonKind.LessThanOrEqual
            or ComparisonKind.GreaterThan or ComparisonKind.GreaterThanOrEqual;

    /// <summary>
    /// #569: an ordering comparison reads a DateTime as the instant it names. <c>Now</c> is one UTC clock
    /// (<c>DateTime.UtcNow</c>), but a value a caller supplies may carry Kind <c>Local</c>, and <c>DateTime</c>'s
    /// operators compare Ticks and ignore Kind — so a local clock time was compared against a UTC one. A Local value is
    /// lowered to its <c>ToUniversalTime()</c>; a <c>Utc</c> or <c>Unspecified</c> value keeps its clock time, the rule
    /// the EF column converter writes and reads (#937). The conditional is part of the module body, which the
    /// interpreter and the printed C# both run, so the two sides cannot disagree.
    /// </summary>
    private Node AsUtcInstant(DomainExpression operand, Node value, List<Node> presenceGuards) {
        if (!TryDateTimeRead(operand, out var optional))
            return value;
        // An ordering against an absent value is false on every door (C#'s lifted comparison), so an optional read is
        // normalized through Value behind the same absence guard the hop reads use.
        if (optional) {
            presenceGuards.Add(new NotEqual(value, new Constant(null!)));
            value = new Member(value, "Value");
        }
        return new Conditional(
            new Equal(
                new Member(value, "Kind"),
                new Member(TypeReference.To<DateTimeKind>(), "Local")),
            new Invoke(new Member(value, "ToUniversalTime")),
            value);
    }

    /// <summary>True when <paramref name="expr"/> reads a DateTime value a caller can hand in with a Kind.</summary>
    private bool TryDateTimeRead(DomainExpression expr, out bool optional) {
        optional = false;
        switch (expr) {
            case PropertyAccess property when IsDateTimeProperty(property.Name):
                if (_actionParameterNames?.Contains(property.Name) != true) {
                    optional = SourceProperty(property.Name)?.Constraints.OfType<OptionalConstraint>().Any() == true;
                    return true;
                }
                return TryDateTimeParameter(property.Name, out optional);
            case ParameterAccess parameter when IsDateTimeProperty(parameter.Name):
                return TryDateTimeParameter(parameter.Name, out optional);
            // An add preserves the Kind of the value it offsets; analysis rejects an offset on an optional date (#648).
            case DateOperation { Kind: not DateOperationKind.DiffDays } operation:
                return TryDateTimeRead(operation.Date, out _);
            default:
                return false;
        }
    }

    /// <summary>The declared parameter of the action being lowered, when it is a DateTime; a name it does not declare
    /// is not a parameter read this pass can type, so it is left as it is.</summary>
    private bool TryDateTimeParameter(string name, out bool optional) {
        optional = false;
        var parameter = CurrentAction()?.Parameters
            .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
        if (parameter is null)
            return false;
        optional = parameter.Constraints.OfType<OptionalConstraint>().Any();
        return true;
    }

    private Poly.DomainModeling.Ontology.Action? CurrentAction() {
        if (_context.ActionName is not { } actionName)
            return null;
        var entity = SourceEntity();
        return entity?.Actions.Concat(entity.Stages.SelectMany(s => s.Actions))
            .FirstOrDefault(a => string.Equals(a.Name, actionName, StringComparison.Ordinal));
    }

    private static bool IsDateTimeType(string? typeName) => typeName is "DateTime" or "Timestamp";

    /// <summary>
    /// The property the subject exposes wins: a path-prefix leaf is typed by the entity the hop landed on
    /// (<see cref="SourceEntity"/>), not by this entity's property that happens to share its name.
    /// </summary>
    private bool IsDateTimeProperty(string name) =>
        IsDateTimeType(SourceProperty(name)?.Type.TypeName ?? _propertyTypeResolver?.Invoke(name));
}