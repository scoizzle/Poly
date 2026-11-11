using Poly.Ast.Nodes;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Lowering;

// Existence: Exists/NotExists as null checks, or a Count check on a collection relationship.

public sealed partial class DomainExpressionLoweringPass {
    protected override LoweredExpression Exists(Exists e) {
        // Collection (`many`) relationship: ctor-initialized lists are never null;
        // store-link presence is a non-empty check. Simulate binds the same Count
        // member through the dictionary nav.
        if (e.Target is PropertyAccess col && IsCollectionNav(col.Name)) {
            var target = Route(e.Target);
            return target with {
                Value = new NotEqual(new Member(target.Value, "Count"), new Constant(0))
            };
        }
        var other = Route(e.Target);
        return other with { Value = new NotEqual(other.Value, new Constant(null)) };
    }

    protected override LoweredExpression NotExists(NotExists ne) {
        if (ne.Target is PropertyAccess col && IsCollectionNav(col.Name)) {
            var target = Route(ne.Target);
            return target with {
                Value = new Equal(new Member(target.Value, "Count"), new Constant(0))
            };
        }
        var other = Route(ne.Target);
        return other with { Value = new Equal(other.Value, new Constant(null)) };
    }
}