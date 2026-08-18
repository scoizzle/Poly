using Poly.DomainModeling.Dispatch;
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