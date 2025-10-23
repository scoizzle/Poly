using Poly.Interpretation;
using Poly.Interpretation.Operators.Boolean;

namespace Poly.Validation.Rules;

public sealed class AndRule(params IEnumerable<Rule> rules) : Rule {
    public IEnumerable<Rule> Rules { get; set; } = rules;

    public override Value BuildInterpretationTree(RuleInterpretationContext context) {
        if (Rules == null || !Rules.Any())
            return new Literal(true);

        var ruleInterpretationTrees = Rules
            .Select(e => e.BuildInterpretationTree(context))
            .ToList();

        if (ruleInterpretationTrees.Count == 1)
            return ruleInterpretationTrees.First();

        return ruleInterpretationTrees
            .Aggregate((current, rule) => new And(current, rule));
    }

    public override string ToString() {
        if (Rules == null || !Rules.Any())
            return "true";

        return string.Join(" and ", Rules);
    }
}