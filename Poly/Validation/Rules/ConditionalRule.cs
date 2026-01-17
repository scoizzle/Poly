using Poly.Interpretation;
using Poly.Interpretation.Expressions;

namespace Poly.Validation.Rules;

public sealed class ConditionalRule : Rule {
    public Rule Condition { get; set; }
    public Rule ThenRule { get; set; }
    public Rule? ElseRule { get; set; }

    public ConditionalRule(Rule condition, Rule thenRule, Rule? elseRule = null)
    {
        Condition = condition;
        ThenRule = thenRule;
        ElseRule = elseRule;
    }

    public override Interpretable BuildInterpretationTree(RuleBuildingContext context)
    {
        var conditionTree = Condition.BuildInterpretationTree(context);
        var thenTree = ThenRule.BuildInterpretationTree(context);

        // If condition is false, the rule passes (using implication: condition -> thenRule)
        // This is logically: !condition OR thenRule
        var implication = new BinaryOperation(
            BinaryOperationKind.Or,
            new UnaryOperation(UnaryOperationKind.Not, conditionTree),
            thenTree
        );

        if (ElseRule != null) {
            var elseTree = ElseRule.BuildInterpretationTree(context);
            // (condition AND thenRule) OR (!condition AND elseRule)
            var conditionalResult = new BinaryOperation(
                BinaryOperationKind.Or,
                new BinaryOperation(BinaryOperationKind.And, conditionTree, thenTree),
                new BinaryOperation(BinaryOperationKind.And, new UnaryOperation(UnaryOperationKind.Not, conditionTree), elseTree)
            );
            return conditionalResult;
        }

        return implication;
    }

    public override string ToString()
    {
        if (ElseRule != null) {
            return $"if ({Condition}) then ({ThenRule}) else ({ElseRule})";
        }
        return $"if ({Condition}) then ({ThenRule})";
    }
}