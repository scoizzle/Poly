namespace Poly.Runtime;

/// <summary>
/// The read-only result surface a lowered domain module names. <see cref="ErrorMessage"/>
/// is a g.3 compatibility shim, removed at R-07; lowering, the VM and the executor reach
/// it through <c>Member(x, "ErrorMessage")</c>, so it is a public member of the concrete
/// records rather than an explicit interface implementation.
/// </summary>
public interface IDomainResult {
    bool IsSuccess { get; }

    DomainFailure? Error { get; }

    string? ErrorMessage { get; }
}