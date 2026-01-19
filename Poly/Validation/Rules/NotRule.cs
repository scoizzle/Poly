using Poly.Interpretation;
using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Interpretation.AbstractSyntaxTree.Boolean;

namespace Poly.Validation.Rules;

public sealed class NotRule(Rule rule) : Rule {
    public Rule Rule { get; set; } = rule ?? throw new ArgumentNullException(nameof(rule));

    public override Node BuildInterpretationTree(RuleBuildingContext context)
    {
        var ruleTree = Rule.BuildInterpretationTree(context);
        var inversion = new Not(ruleTree);
        return inversion;
    }

    public override string ToString() => $"!({Rule})";
}