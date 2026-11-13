using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Meaning;
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
                new Assignment(result, new Constant(SumSeed(s))),
                new ForEachLoop(item, collection,
                    new Block([
                        new Assignment(result, new SN.Add(result, new Member(item, s.PropertyName))),
                    ])),
            ],
            [result],
            result);
    }

    /// <summary>
    /// The accumulator's zero in the sum's own type (#510): <see cref="DomainTypeModel.SumResult"/> over the
    /// session's type maps, the type analysis gives the sum. A property the model cannot type keeps <c>0L</c>;
    /// analysis reports a non-numeric sum.
    /// </summary>
    private object SumSeed(SumExpr sum) {
        var target = ResolveRelationshipTarget(sum.RelationshipName);
        var property = _domain.Types.OfType<Entity>()
            .FirstOrDefault(e => string.Equals(e.Name, target, StringComparison.Ordinal))?
            .Properties.FirstOrDefault(p => string.Equals(p.Name, sum.PropertyName, StringComparison.Ordinal));
        if (property is null) return 0L;
        var model = new DomainTypeModel(_domain, RuntimeAnalysisCache.Session(_domain).TypeMaps);
        return model.KindOf(model.SumResult(model.OfProperty(property))) switch {
            NumericKind.Decimal => 0m,
            NumericKind.Float64 => 0.0,
            _ => 0L,
        };
    }

    private enum QuantifierKind { Any, All, None, Count }

    /// <summary>
    /// One quantifier: <c>result = init; foreach (var itemN in collection) { body; if (...) }</c>,
    /// value is <c>result</c>.
    /// <list type="bullet">
    /// <item><c>any</c>: true at the first match; false when nothing matches or there are no items.</item>
    /// <item><c>all</c>: false at the first miss; also false when there are no items (via sawItem).</item>
    /// <item><c>none</c>: false at the first match; true otherwise.</item>
    /// <item><c>count … where</c>: the number of matching items.</item>
    /// </list>
    /// </summary>
    private LoweredExpression LowerFilteredQuantifier(
        string relationshipName, DomainExpression body, QuantifierKind kind) {
        var collection = new Member(_currentSubject, ResolveNavName(relationshipName));
        var item = _names.Next("item");
        var bodyLowered = LowerQuantifierBody(relationshipName, body, item);

        var result = _names.Next(kind switch {
            QuantifierKind.Any => "any",
            QuantifierKind.All => "all",
            QuantifierKind.None => "none",
            _ => "count",
        });
        Node initial = kind switch {
            QuantifierKind.Any => new Constant(false),
            QuantifierKind.All => new Constant(true),
            QuantifierKind.None => new Constant(true),
            _ => new Constant(0L),
        };
        Node onItem = kind switch {
            QuantifierKind.Any => new IfStatement(bodyLowered.Value, new Block([
                new Assignment(result, new Constant(true)), new BreakStatement()])),
            QuantifierKind.All => new IfStatement(new SN.Not(bodyLowered.Value), new Block([
                new Assignment(result, new Constant(false)), new BreakStatement()])),
            QuantifierKind.None => new IfStatement(bodyLowered.Value, new Block([
                new Assignment(result, new Constant(false)), new BreakStatement()])),
            _ => new IfStatement(bodyLowered.Value, new Block([
                new Assignment(result, new SN.Add(result, new Constant(1L)))])),
        };

        if (kind == QuantifierKind.All) {
            // `all` over no items is false, so the loop also records that it saw one.
            var sawItem = _names.Next("sawItem");
            Node[] statements = [
                new Assignment(result, initial),
                new Assignment(sawItem, new Constant(false)),
                new ForEachLoop(item, collection,
                    new Block(
                        [.. bodyLowered.Statements, new Assignment(sawItem, new Constant(true)), onItem],
                        bodyLowered.Variables)),
            ];
            return new LoweredExpression(statements, [result, sawItem], new SN.And(sawItem, result));
        }

        Node[] loopStatements = [
            new Assignment(result, initial),
            new ForEachLoop(item, collection,
                new Block([.. bodyLowered.Statements, onItem], bodyLowered.Variables)),
        ];
        return new LoweredExpression(loopStatements, [result], result);
    }

    /// <summary>
    /// Lowers a quantifier body against the loop's <paramref name="item"/>, using the
    /// related entity's enum properties. Nested quantifiers in the body produce
    /// statements that run inside the outer loop, once per item, and share this
    /// pass's local-name generator so names stay unique.
    /// </summary>
    private LoweredExpression LowerQuantifierBody(
        string relationshipName, DomainExpression body, Variable item) =>
        LowerAgainstEntity(ResolveRelationshipTarget(relationshipName), body, item);
}