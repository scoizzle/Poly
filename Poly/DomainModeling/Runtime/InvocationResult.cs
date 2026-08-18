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

namespace Poly.DomainModeling.Runtime;

/// <summary>
/// Describes the output(s) produced by an <see cref="Action"/> or <see cref="Effect"/>.
/// </summary>
public sealed record InvocationResult(IReadOnlyList<InvocationResult.Member> Members) : DomainObject {
    public static readonly InvocationResult Void = new([]);

    public sealed record Member(string Name, DomainTypeReference Type, IReadOnlyList<Constraint> Constraints) : DomainObject {
        public override IEnumerable<Node?> Children => [.. Constraints];
    }

    public override IEnumerable<Node?> Children => Members;
}