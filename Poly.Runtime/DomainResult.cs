namespace Poly.Runtime;

/// <summary>
/// The void result of a lowered domain action. It carries no <c>Value</c>: a non-void
/// action returns <see cref="DomainResult{T}"/> instead.
/// </summary>
public sealed record DomainResult : IDomainResult {
    /// <summary>True when the action succeeded; false when it carries a <see cref="Error"/>.</summary>
    public bool IsSuccess { get; }

    /// <summary>The failure of a failed result; null on success.</summary>
    public DomainFailure? Error { get; }

    /// <summary>g.3 shim; removed at R-07.</summary>
    public string? ErrorMessage => Error?.Render();

    private DomainResult(bool isSuccess, DomainFailure? error) {
        IsSuccess = isSuccess;
        Error = error;
    }

    /// <summary>Creates a successful result.</summary>
    public static DomainResult Success() => new(true, null);

    /// <summary>Creates a failed result carrying <paramref name="error"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is null.</exception>
    public static DomainResult Failure(DomainFailure error) =>
        new(false, error ?? throw new ArgumentNullException(nameof(error)));

    /// <summary>The g.3 text-compatible shim; removed at R-07. Produces <see cref="DomainFailureKind.Constraint"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
    public static DomainResult Failure(string message) =>
        new(false, DomainFailure.Constraint(message ?? throw new ArgumentNullException(nameof(message))));
}