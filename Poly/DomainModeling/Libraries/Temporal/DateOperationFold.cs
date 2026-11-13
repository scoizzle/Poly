using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Libraries.Temporal;

/// <summary>
/// Pack-owned claim on the operator table's <c>+</c> and <c>-</c> rows: a clock date node (<c>Now</c> / <c>today</c>), a
/// date property (<c>DueDate</c>), or an already-folded <see cref="DateOperation"/> combined
/// with a parsed <see cref="Duration"/> folds to a <see cref="DateOperation"/> (subtract
/// negates the offset). Chained offsets nest (<c>Now + 2 Hours + 3 Minutes</c>). Any
/// other combination returns null so the row keeps the plain arithmetic node. The match is type-blind by design (the
/// parser has no types); analysis rejects a left operand that is not a date.
/// </summary>
public static class DateOperationFold {
    public static DomainExpression? Plus(DomainExpression left, DomainExpression right) => Fold(left, right, isPlus: true);

    public static DomainExpression? Minus(DomainExpression left, DomainExpression right) => Fold(left, right, isPlus: false);

    private static DomainExpression? Fold(DomainExpression left, DomainExpression right, bool isPlus) {
        if (left is not (Now or Today or PropertyAccess or DateOperation) || right is not Duration d)
            return null;

        var offset = isPlus ? d.Amount : -d.Amount;
        return new DateOperation(
            left,
            DomainExpression.Literal(offset, new DomainTypeReference("Number")),
            DurationForm.ToKind(d.Unit));
    }
}