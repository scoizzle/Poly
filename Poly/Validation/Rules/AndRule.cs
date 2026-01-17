using Poly.Interpretation;
using Poly.Interpretation.Expressions;

namespace Poly.Validation.Rules;

public sealed class AndRule(params IEnumerable<Rule> rules) : Rule {
    public IEnumerable<Rule> Rules { get; set; } = rules;

    public override Interpretable BuildInterpretationTree(RuleBuildingContext context)
    {
        if (Rules == null || !Rules.Any())
            return new Constant(true);

        var ruleInterpretationTrees = Rules
            .Select(e => e.BuildInterpretationTree(context))
            .ToList();

        if (ruleInterpretationTrees.Count == 1)
            return ruleInterpretationTrees.First();

        var combinedRules = ruleInterpretationTrees
            .Aggregate((current, rule) => new BinaryOperation(BinaryOperationKind.And, current, rule));

        return combinedRules;
    }

    public override string ToString()
    {
        if (Rules == null || !Rules.Any())
            return "true";

        return string.Join(" and ", Rules);
    }
}