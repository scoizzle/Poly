using Poly.Interpretation;
using Poly.Interpretation.Operators.Boolean;

namespace Poly.Validation.Rules;

public sealed class OrRule(params IEnumerable<Rule> rules) : Rule {
    public IEnumerable<Rule> Rules { get; set; } = rules;

    public override Value BuildInterpretationTree(RuleBuildingContext context) {
        if (Rules == null || !Rules.Any())
            return context.Test(new Literal(false));

        var combinedRules = Rules
            .Select(e => e.BuildInterpretationTree(context))
            .Aggregate(Literal.False, (current, rule) => new Or(current, rule));
        
        return context.Test(combinedRules);
    }

    public override string ToString() {
        if (Rules == null || !Rules.Any())
            return "false";

        return string.Join(" or ", Rules);
    }
}