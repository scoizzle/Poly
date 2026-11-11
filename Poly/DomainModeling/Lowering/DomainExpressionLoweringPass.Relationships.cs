using Poly.Ast.Nodes;
using Poly.DomainModeling.Ontology;

using SN = Poly.Ast.Nodes;

namespace Poly.DomainModeling.Lowering;

// Relationship navigation: path-prefix hops, binder roots, and the unlinked-treats-as-false predicate shape.

public sealed partial class DomainExpressionLoweringPass {
    protected override LoweredExpression RelationshipNavigation(RelationshipNavigation rn) {
        // Peer binder / other parameter-backed path-prefix roots: subject is the
        // parameter node, not Member(this, name). Nested path-prefix under that
        // root is unsupported (analysis rejects; fail loud here for defense).
        if (_parameters.TryGetValue(rn.RelationshipName, out var parameterSubject)) {
            if (ContainsRelationshipNavigation(rn.TargetProperty)) {
                throw new InvalidOperationException(
                    $"Nested path-prefix under binder '{rn.RelationshipName}' is not supported. " +
                    "Use a single peer property (e.g. 'order Code'), not nested navigation.");
            }
            return Route(rn.TargetProperty, parameterSubject);
        }

        // Every hop in a path-prefix is a relationship navigation.
        // Predicate leaves: an unlinked to-one is false. A statement-free predicate
        // leaf is `rel != null && <leaf>`. When the leaf has statements (a quantifier
        // on the target), those statements run only inside `if (rel != null)` after
        // a temp is set to false. Value leaves stay the hop. NullForgiving on the
        // hop is CS8602 only; require gates own DomainResult.Failure
        // ("requires a linked") in BuildActionBodyWithGuards.
        // Collection hops cannot be a singular path-prefix — fail closed (use any/all).
        if (IsCollectionNav(rn.RelationshipName)) {
            throw new InvalidOperationException(
                $"Path-prefix on relationship '{rn.RelationshipName}' requires exactly one linked target. " +
                "Use any/all quantifiers for collections.");
        }
        var relMember = new Member(_currentSubject, ResolveNavName(rn.RelationshipName));
        var leaf = LowerAgainstEntity(
            ResolveRelationshipTarget(rn.RelationshipName),
            rn.TargetProperty,
            new NullForgiving(relMember));
        if (!IsPathPrefixPredicate(rn.TargetProperty))
            return leaf;
        var exists = new NotEqual(relMember, new Constant(null));
        if (leaf.Statements.Count == 0)
            return leaf with { Value = new SN.And(exists, leaf.Value) };
        var t = _names.Next("t");
        var thenBlock = new Block(
            [.. leaf.Statements, new Assignment(t, leaf.Value)],
            leaf.Variables);
        return new LoweredExpression(
            [new Assignment(t, new Constant(false)), new IfStatement(exists, thenBlock)],
            [t],
            t);
    }
}