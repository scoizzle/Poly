using Poly.DomainModeling.Ontology;

using AccessModifier = Poly.Introspection.AccessModifier;
using PrimType = Poly.Introspection.PrimitiveType;
using Syntactic = Poly.Ast.Nodes;

namespace Poly.DomainModeling.Lowering;

// Invariants: policy methods, unique-property invariants, and entity-level guard methods.
public sealed partial class DomainToCSharpExporter {
    /// <summary>
    /// Assign of a unique property calls <c>this.EnsureUnique</c>. The same value
    /// already on this instance is Success. A different value fails closed: this method holds
    /// no storage port. The storage library rewrites every EnsureUnique-then-assign into
    /// <c>db.Exists</c>/<c>Release</c>/<c>Record</c> on the <c>IStoragePort</c> the action receives,
    /// so a module compiled with storage never reaches this failure.
    /// </summary>
    private static MethodDefinitionNode EnsureUniqueAssignMethod(Entity entity) {
        var propertyName = new Parameter("propertyName", new PrimitiveTypeReference(PrimType.String));
        var value = new Parameter("value", StoreJobObjectType());
        var nodes = new List<Node> {
            new IfStatement(
                new Equal(value, new Constant(null)),
                new Block([DomainResultSuccess()]))
        };
        foreach (var prop in entity.Properties.OrderBy(p => p.Name, StringComparer.Ordinal)) {
            if (!prop.Constraints.OfType<UniqueConstraint>().Any())
                continue;
            nodes.Add(new IfStatement(
                new Syntactic.And(
                    new Equal(propertyName, new Constant(prop.Name)),
                    new Invoke(
                        new Member(new ClrTypeReference(typeof(object)), "Equals"),
                        new Member(new ThisReference(), prop.Name),
                        value)),
                new Block([DomainResultSuccess()])));
        }
        nodes.Add(new Return(new Invoke(
            new Member(new NamedTypeReference("DomainResult"), "Failure"),
            new Constant("A unique assign needs the storage port."))));
        return new MethodDefinitionNode(
            "EnsureUnique",
            new NamedTypeReference("DomainResult"),
            Parameters: [propertyName, value],
            Body: new Block(nodes),
            AccessModifier: AccessModifier.Public);
    }

    private static Return DomainResultSuccess() =>
        new(new Invoke(new Member(new NamedTypeReference("DomainResult"), "Success")));
}