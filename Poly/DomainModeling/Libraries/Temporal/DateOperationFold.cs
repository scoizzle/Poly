using System.Globalization;

using Poly.DomainModeling.Dispatch;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;
using Poly.DomainModeling.Runtime;

using Action = Poly.DomainModeling.Ontology.Action;
using Add = Poly.DomainModeling.Ontology.Add;
using And = Poly.DomainModeling.Ontology.And;
using Divide = Poly.DomainModeling.Ontology.Divide;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Not = Poly.DomainModeling.Ontology.Not;
using Or = Poly.DomainModeling.Ontology.Or;
using PrimitiveType = Poly.DomainModeling.Ontology.PrimitiveType;
using Subtract = Poly.DomainModeling.Ontology.Subtract;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.DomainModeling.Libraries.Temporal;

/// <summary>
/// Pack-owned binary fold: a clock date node (<c>Now</c> / <c>today</c>) or a date
/// property (<c>DueDate</c>) combined with a parsed <see cref="Duration"/> folds straight
/// to a <see cref="DateOperation"/> (subtract negates the offset). Any other combination
/// returns null so the core parser keeps the plain arithmetic node.
/// </summary>
public sealed class DateOperationFold : IBinaryExpressionFold {
    public DomainExpression? TryFold(DomainExpression left, DomainExpression right, bool isPlus) {
        if (left is not (Now or Today or PropertyAccess) || right is not Duration d)
            return null;

        var offset = isPlus ? d.Amount : -d.Amount;
        var kind = d.Unit switch {
            DurationUnit.Days => DateOperationKind.AddDays,
            DurationUnit.Months => DateOperationKind.AddMonths,
            _ => throw new NotSupportedException($"Duration unit '{d.Unit}' cannot fold to a date operation."),
        };
        return new DateOperation(left, DomainExpression.Literal(offset, new DomainTypeReference("Number")), kind);
    }
}