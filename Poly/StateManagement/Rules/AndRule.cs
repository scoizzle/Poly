using Poly.Interpretation;
using Poly.Interpretation.Operators.Boolean;

namespace Poly.StateManagement.Validation.Rules;

public sealed record AndRule(params IEnumerable<Rule> rules) : Rule
{
    public IEnumerable<Rule> Rules { get; set; } = rules;

    public override Value BuildInterpretationTree(RuleInterpretationContext context)
    {
        if (Rules == null || !Rules.Any())
            return new Literal(true);

        return Rules
            .Select(e => e.BuildInterpretationTree(context))
            .Aggregate<Value, Value>(Literal.True, (current, rule) => new And(current, rule));
    }

    public override string ToString()
    {
        if (Rules == null || !Rules.Any())
            return "true";

        return string.Join(" and ", Rules);
    }
}
