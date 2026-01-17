using Poly.Interpretation;
using Poly.Interpretation.Expressions;

namespace Poly.Validation.Rules;

public sealed class NotRule(Rule rule) : Rule {
    public Rule Rule { get; set; } = rule ?? throw new ArgumentNullException(nameof(rule));

    public override Interpretable BuildInterpretationTree(RuleBuildingContext context)
    {
        var ruleTree = Rule.BuildInterpretationTree(context);
        var inversion = new UnaryOperation(UnaryOperationKind.Not, ruleTree);
        return inversion;
    }

    public override string ToString() => $"!({Rule})";
}