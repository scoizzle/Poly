namespace Poly.DomainModeling.Runtime;

/// <summary>
/// Fail-closed constraint or create-job failure in a void export body
/// (OnEntry, ctor, subscription). Does not subclass
/// <see cref="InvalidOperationException"/> so host fail-loud throws still escape.
/// </summary>
public sealed class ConstraintFailureException : Exception {
    public ConstraintFailureException(string message) : base(message) { }
}
