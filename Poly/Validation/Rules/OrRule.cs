using Poly.Interpretation;
using Poly.Interpretation.Expressions;

namespace Poly.Validation.Rules;

public sealed class OrRule(params IEnumerable<Rule> rules) : Rule {
    public IEnumerable<Rule> Rules { get; set; } = rules;

    public override Interpretable BuildInterpretationTree(RuleBuildingContext context)
    {
        if (Rules == null || !Rules.Any())
            return new Constant(false);

        var combinedRules = Rules
            .Select(e => e.BuildInterpretationTree(context))
            .Aggregate((Interpretable)new Constant(false), (current, rule) => new BinaryOperation(BinaryOperationKind.Or, current, rule));

        return combinedRules;
    }

    public override string ToString()
    {
        if (Rules == null || !Rules.Any())
            return "false";

        return string.Join(" or ", Rules);
    }
}