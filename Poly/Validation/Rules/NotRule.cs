using Poly.Interpretation;
using Poly.Interpretation.Operators.Boolean;

namespace Poly.Validation.Rules;

public sealed class NotRule(Rule rule) : Rule {
    public Rule Rule { get; set; } = rule ?? throw new ArgumentNullException(nameof(rule));

    public override Value BuildInterpretationTree(RuleBuildingContext context) => context.Test(new Not(Rule.BuildInterpretationTree(context)));

    public override string ToString() => $"!({Rule})";
}