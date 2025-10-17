using Poly.Interpretation;
using Poly.Interpretation.Operators.Boolean;

namespace Poly.StateManagement.Validation.Rules;

public sealed record NotRule(Rule rule) : Rule {
    public Rule Rule { get; set; } = rule ?? throw new ArgumentNullException(nameof(rule));

    public override Value BuildInterpretationTree(RuleInterpretationContext context) => new Not(Rule.BuildInterpretationTree(context));

    public override string ToString() => $"!({Rule})";
}