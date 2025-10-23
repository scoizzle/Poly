using Poly.Validation.Rules;

namespace Poly.Validation;

public sealed class RuleSet<T> {
    public RuleSet(IEnumerable<Rule> rules) {
        CombinedRules = new AndRule(rules);

        var ruleInterpretationContext = new RuleInterpretationContext<T>();
        RuleSetInterpretation = CombinedRules.BuildInterpretationTree(ruleInterpretationContext);
        ExpressionTree = ruleInterpretationContext.BuildExpression(CombinedRules);

        var paramInterpretation = ruleInterpretationContext.GetParameterExpression();
        var lambda = Expression.Lambda<Predicate<T>>(ExpressionTree, paramInterpretation);
        Predicate = lambda.Compile();
    }

    public Rule CombinedRules { get; }
    public Interpretation.Value RuleSetInterpretation { get; }
    public Expression ExpressionTree { get; }
    public Predicate<T> Predicate { get; }

    public bool Test(T instance) => Predicate(instance);
}