using Poly.Interpretation;

namespace Poly.StateManagement;

public abstract record Rule {
    public abstract Value BuildInterpretationTree(RuleInterpretationContext context);
}