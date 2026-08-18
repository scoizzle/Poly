using Poly.DomainModeling;
using Poly.DomainModeling.Libraries.Temporal;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Runtime;

/// <summary>
/// Review F7 / Issue 8: <see cref="DomainExpressionRewriteBase"/> must route every
/// known <see cref="DomainExpression"/> subtype through its identity/recursion
/// overrides — a full-tree identity rewrite round-trips any expression unchanged.
/// A future subtype that the base fails to handle fails loud in
/// <see cref="DomainExpressionRewriteBase"/> Default (no silent pass-through).
/// </summary>
public class DomainExpressionRewriteIdentityTests {
    private sealed class IdentityRewrite : DomainExpressionRewriteBase { }

    /// <summary>Builds a tree exercising every DomainExpression subtype (20/20).</summary>
    private static DomainExpression BuildAllSubtypes() {
        var lit = DomainExpression.Literal("Open");                              // Literal
        var sum = DomainExpression.Add(lit, lit);                                // Add
        var diff = DomainExpression.Subtract(sum, lit);                          // Subtract
        var prod = DomainExpression.Multiply(diff, lit);                         // Multiply
        var quot = DomainExpression.Divide(prod, lit);                           // Divide
        var date = new DateOperation(lit, lit, DateOperationKind.AddDays); // DateOperation
        var nav = DomainExpression.RelationshipNav("orders",
            DomainExpression.Property("Total"));                                 // RelationshipNavigation
        var exists = DomainExpression.Exists(nav);                               // Exists
        var notExists = DomainExpression.NotExists(nav);                         // NotExists
        var owned = DomainExpression.Owned("Addr",
            DomainExpression.Property("Street"));                                // OwnedAccess
        var any = DomainExpression.Any("orders",
            DomainExpression.GreaterThan(DomainExpression.Property("Total"), lit)); // AnyExpr + Comparison
        var all = DomainExpression.All("orders",
            DomainExpression.Equal(DomainExpression.Property("Status"), lit));   // AllExpr
        var none = DomainExpression.None("orders", DomainExpression.Property("Status")); // NoneExpr
        var count = DomainExpression.Count("orders", DomainExpression.Property("Total")); // CountExpr
        var and = DomainExpression.And(any, all);                                // And
        var or = DomainExpression.Or(and, none);                                 // Or
        var not = DomainExpression.Not(quot);                                    // Not
        var eq = DomainExpression.Equal(not, exists);                            // Comparison
        return DomainExpression.And(
            DomainExpression.Or(eq, notExists),
            DomainExpression.And(owned, count));
    }

    [Test]
    public async Task IdentityRewrite_RoutesEverySubtype_ReturnsEqualTree() {
        var original = BuildAllSubtypes();

        var routed = new IdentityRewrite().Route(original);

        await Assert.That(routed).IsEqualTo(original);
    }

    [Test]
    public async Task IdentityRewrite_BareCount_ReturnsEqualNode() {
        var bare = DomainExpression.Count("orders", null);

        await Assert.That(new IdentityRewrite().Route(bare)).IsEqualTo(bare);
    }

    [Test]
    public async Task IdentityRewrite_LeafNodes_ReturnIdentity() {
        // Leaf identity must return the very same node instance (no copy).
        // (Two separately constructed nodes differ only in Node Id — compare by reference.)
        var rewrite = new IdentityRewrite();
        var prop = DomainExpression.Property("X");
        var param = DomainExpression.Parameter("p");
        var lit = DomainExpression.Literal(3);

        await Assert.That(rewrite.Route(prop)).IsSameReferenceAs(prop);
        await Assert.That(rewrite.Route(param)).IsSameReferenceAs(param);
        await Assert.That(rewrite.Route(lit)).IsSameReferenceAs(lit);
    }
}