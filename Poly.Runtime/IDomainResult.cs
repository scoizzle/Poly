namespace Poly.Runtime;

/// <summary>
/// The read-only result surface a lowered domain module names. <see cref="ErrorMessage"/>
/// is a g.3 compatibility shim, removed at R-07; lowering, the VM and the executor reach
/// it through <c>Member(x, "ErrorMessage")</c>, so it is a public member of the concrete
/// records rather than an explicit interface implementation.
/// </summary>
public interface IDomainResult {
    /// <summary>True when the action succeeded; false when it carries a <see cref="Error"/>.</summary>
    bool IsSuccess { get; }

    /// <summary>The failure of a failed result; null on success.</summary>
    DomainFailure? Error { get; }

    /// <summary>g.3 shim; removed at R-07.</summary>
    string? ErrorMessage { get; }
}