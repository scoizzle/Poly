namespace Poly.Data.Modeling;

public sealed class DomainMutationValidationException : InvalidOperationException {
    public DomainMutationValidationException(string mutationName, IReadOnlyList<Diagnostic> diagnostics)
        : base($"Mutation '{mutationName}' violated domain invariants.") {
        MutationName = mutationName;
        Diagnostics = diagnostics;
    }

    public string MutationName { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
}