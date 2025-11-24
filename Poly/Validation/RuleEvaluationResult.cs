namespace Poly.Validation;

public sealed record RuleEvaluationResult(IEnumerable<ValidationError> Errors) {
    public bool IsValid => !Errors.Any();
}