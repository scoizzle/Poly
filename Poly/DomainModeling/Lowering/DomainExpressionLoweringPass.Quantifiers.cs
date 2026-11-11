using Poly.Ast.Nodes;
using Poly.DomainModeling.Ontology;

using SN = Poly.Ast.Nodes;

namespace Poly.DomainModeling.Lowering;

// Quantifiers: any, all, none, count and sum over a relationship collection.

public sealed partial class DomainExpressionLoweringPass {
    // Filtered any/all/none/count: a foreach over the navigation collection
    // (the same Member(subject, PascalCase(rel)) that ForEachInvoke loops over).
    // The quantifier's statements are the init + loop; its value is the result variable
    // (or sawItem && result for all, so an empty collection is false).
    // Bare `count Rel` is the collection's Count.
    protected override LoweredExpression AnyExpr(AnyExpr a) =>
        LowerFilteredQuantifier(a.RelationshipName, a.Body, QuantifierKind.Any);

    protected override LoweredExpression AllExpr(AllExpr a) =>
        LowerFilteredQuantifier(a.RelationshipName, a.Body, QuantifierKind.All);

    protected override LoweredExpression NoneExpr(NoneExpr n) =>
        LowerFilteredQuantifier(n.RelationshipName, n.Body, QuantifierKind.None);

    protected override LoweredExpression CountExpr(CountExpr c) {
        if (c.Body is null)
            return LoweredExpression.Of(new Member(
                new Member(_currentSubject, ResolveNavName(c.RelationshipName)),
                "Count"));
        return LowerFilteredQuantifier(c.RelationshipName, c.Body, QuantifierKind.Count);
    }

    protected override LoweredExpression SumExpr(SumExpr s) {
        var collection = new Member(_currentSubject, ResolveNavName(s.RelationshipName));
        var item = _names.Next("item");
        var result = _names.Next("result");
        return new LoweredExpression(
            [
                new Assignment(result, new Constant(0L)),
                new ForEachLoop(item, collection,
                    new Block([
                        new Assignment(result, new SN.Add(result, new Member(item, s.PropertyName))),
                    ])),
            ],
            [result],
            result);
    }
}