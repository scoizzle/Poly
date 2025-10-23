namespace Poly.Validation;

public abstract class Rule {
    public abstract Interpretation.Value BuildInterpretationTree(RuleInterpretationContext context);
}

public sealed class RuleEvaluationResult {
    private readonly List<string> _violations = [];

    public IEnumerable<string> Violations => _violations;
    public bool IsValid => _violations.Count == 0;

    public void AddError(string violation) {
        _violations.Add(violation);
    }
}
