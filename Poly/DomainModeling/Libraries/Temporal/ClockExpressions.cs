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

/// <summary>
/// The current clock instant (UTC). Platform-agnostic clock IR; the CLR host
/// lowers it to <c>DateTime.UtcNow</c> and the store/preprocess path resolves it
/// via an injectable clock (p1 T3).
/// </summary>
public sealed record Now : DomainExpression {
    public sealed override IEnumerable<Node?> Children => [];
}

/// <summary>
/// Today's date (UTC). Platform-agnostic clock IR; the CLR host lowers it to
/// <c>DateOnly.FromDateTime(DateTime.UtcNow)</c>.
/// </summary>
public sealed record Today : DomainExpression {
    public sealed override IEnumerable<Node?> Children => [];
}