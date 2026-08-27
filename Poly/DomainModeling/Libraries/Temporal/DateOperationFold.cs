using Poly.DomainModeling.Ontology;

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
        return new DateOperation(
            left,
            DomainExpression.Literal(offset, new DomainTypeReference("Number")),
            DurationForm.ToKind(d.Unit));
    }
}