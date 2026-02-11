using Poly.Interpretation.AbstractSyntaxTree.Boolean;

using static Poly.Interpretation.AbstractSyntaxTree.NodeExtensions;

namespace Poly.Validation.Rules;

public sealed class OrRule(params IEnumerable<Rule> rules) : Rule {
    public IEnumerable<Rule> Rules { get; set; } = rules;

    public override Node BuildInterpretationTree(RuleBuildingContext context)
    {
        if (Rules == null || !Rules.Any())
            return Wrap(false);

        var combinedRules = Rules
            .Select(e => e.BuildInterpretationTree(context))
            .Aggregate((Node)False, (current, rule) => new Or(current, rule));

        return combinedRules;
    }

    public override string ToString()
    {
        if (Rules == null || !Rules.Any())
            return "false";

        return string.Join(" or ", Rules);
    }
}