using System.Text;

namespace Poly.Runtime;

/// <summary>
/// The typed result of a non-void lowered action. <see cref="Value"/> is non-nullable
/// <typeparamref name="T"/> and throws on a failed result rather than exposing
/// <c>default!</c>. The private <see cref="PrintMembers"/> keeps <c>ToString</c> from
/// reading <see cref="Value"/> on a failure.
/// </summary>
public sealed record DomainResult<T> : IDomainResult {
    public bool IsSuccess { get; }

    public DomainFailure? Error { get; }

    public string? ErrorMessage => Error?.Render();

    private readonly T _value = default!;

    /// <exception cref="InvalidOperationException">The result failed.</exception>
    public T Value => IsSuccess
        ? _value
        : throw new InvalidOperationException("A failed DomainResult has no Value.");

    private DomainResult(bool isSuccess, T value, DomainFailure? error) {
        IsSuccess = isSuccess;
        _value = value;
        Error = error;
    }

    public static DomainResult<T> Success(T value) => new(true, value, null);

    /// <exception cref="ArgumentNullException"><paramref name="error"/> is null.</exception>
    public static DomainResult<T> Failure(DomainFailure error) =>
        new(false, default!, error ?? throw new ArgumentNullException(nameof(error)));

    /// <summary>The g.3 text-compatible shim; removed at R-07. Produces <see cref="DomainFailureKind.Constraint"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
    public static DomainResult<T> Failure(string message) =>
        new(false, default!, DomainFailure.Constraint(message ?? throw new ArgumentNullException(nameof(message))));

    public bool TryGetValue(out T value) {
        if (IsSuccess) {
            value = _value;
            return true;
        }

        value = default!;
        return false;
    }

    private bool PrintMembers(StringBuilder builder) {
        builder.Append("IsSuccess = ").Append(IsSuccess);
        if (IsSuccess) {
            builder.Append(", Value = ").Append((object?)Value);
        }
        else {
            builder.Append(", Error = ").Append(Error?.Render());
        }

        return true;
    }
}