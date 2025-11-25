namespace Poly.Validation;

public sealed class RuleEvaluationContext {
    private readonly List<ValidationError> _errors = new();

    public IEnumerable<ValidationError> Errors => _errors;

    public void AddError(ValidationError error) {
        ArgumentNullException.ThrowIfNull(error);
        _errors.Add(error);
    }

    public bool Evaluate(bool condition) {
        if (!condition) {
            AddError(new ValidationError("", "", "Validation failed."));
        }
        return condition;
    }

    public RuleEvaluationContext Evaluate(bool condition, Func<ValidationError> errorFactory) {
        ArgumentNullException.ThrowIfNull(errorFactory);
        if (!condition) {
            var error = errorFactory();
            AddError(error);
        }
        return this;
    }

    public RuleEvaluationResult GetResult() => new RuleEvaluationResult(_errors);
}