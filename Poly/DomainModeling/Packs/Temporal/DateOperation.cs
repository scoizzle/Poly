namespace Poly.DomainModeling.Packs.Temporal;

public enum DateOperationKind { AddDays, AddMonths, DiffDays }

/// <summary>
/// Resolved date arithmetic (<c>Now - 12 days</c>, <c>DueDate + 14 days</c>).
/// Core consumers (dispatch, rewrite, lowering, analysis) route it through the
/// <see cref="DomainExpression"/> seam; the temporal pack owns the spelling that
/// produces it and the print binder that re-emits it.
/// </summary>
public sealed record DateOperation(
    DomainExpression Date,
    DomainExpression Offset,
    DateOperationKind Kind
) : DomainExpression {
    public sealed override IEnumerable<Node?> Children => [Date, Offset];
}