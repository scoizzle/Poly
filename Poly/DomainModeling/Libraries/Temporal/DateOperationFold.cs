using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Libraries.Temporal;

/// <summary>
/// Pack-owned binary fold: a clock date node (<c>Now</c> / <c>today</c>), a date
/// property (<c>DueDate</c>), or an already-folded <see cref="DateOperation"/> combined
/// with a parsed <see cref="Duration"/> folds to a <see cref="DateOperation"/> (subtract
/// negates the offset). Chained offsets nest (<c>Now + 2 Hours + 3 Minutes</c>). Any
/// other combination returns null so the core parser keeps the plain arithmetic node.
/// </summary>
public sealed class DateOperationFold : IBinaryExpressionFold {
    public DomainExpression? TryFold(DomainExpression left, DomainExpression right, bool isPlus) {
        if (left is not (Now or Today or PropertyAccess or DateOperation) || right is not Duration d)
            return null;

        var offset = isPlus ? d.Amount : -d.Amount;
        return new DateOperation(
            left,
            DomainExpression.Literal(offset, new DomainTypeReference("Number")),
            DurationForm.ToKind(d.Unit));
    }
}