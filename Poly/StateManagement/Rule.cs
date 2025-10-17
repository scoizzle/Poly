namespace Poly.StateManagement;

public abstract record Rule {
    public abstract Interpretation.Value BuildInterpretationTree(RuleInterpretationContext context);
}