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
        if (IsPathPrefixPredicate(rn.TargetProperty)) {
            // A navigation to a condition reads its subject through the hop, so the subject is absent when
            // the hop is unlinked (#936). The condition answers that absence itself: a comparison's `!=` is
            // true, an ordering false, `is null` true. No `hop != null &&` wrapper, which would answer false
            // for every operator. Statements (a quantifier leaf) keep the fail-closed shape: false while unlinked.
            var hopGuard = new NotEqual(relMember, new Constant(null));
            Node hopPresence = _subjectPresence is null
                ? hopGuard
                : new SN.And(_subjectPresence, hopGuard);
            var condition = LowerAgainstEntity(
                ResolveRelationshipTarget(rn.RelationshipName),
                rn.TargetProperty,
                new NullForgiving(relMember),
                condition: true,
                subjectPresence: hopPresence);
            if (condition.Statements.Count == 0)
                return condition;
            var t = _names.Next("t");
            var thenBlock = new Block(
                [.. condition.Statements, new Assignment(t, condition.Value)],
                condition.Variables);
            return new LoweredExpression(
                [new Assignment(t, new Constant(false)), new IfStatement(hopPresence, thenBlock)],
                [t],
                t);
        }
        return LowerAgainstEntity(
            ResolveRelationshipTarget(rn.RelationshipName),
            rn.TargetProperty,
            new NullForgiving(relMember));
    }

    private static bool ContainsRelationshipNavigation(DomainExpression expr) =>
        expr is RelationshipNavigation
        || expr.Children.OfType<DomainExpression>().Any(ContainsRelationshipNavigation);
}