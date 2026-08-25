namespace Poly.DomainModeling.Runtime;

/// <summary>
/// VM target for the shared <c>DomainResult</c> tree (Failure guard, IsSuccess
/// fail-fast after invoke). Generated C# has its own <c>DomainResult</c> that
/// returns a result object; this type throws on <see cref="Failure"/> and
/// returns a live instance from <see cref="Success"/> so the same tree is
/// fail-closed when the VM runs it. Same tree, different <c>DomainResult</c>
/// — like Notify on This.
/// </summary>
public sealed class DomainResult {
    public bool IsSuccess { get; }
    public string? ErrorMessage { get; }

    private DomainResult(bool isSuccess, string? errorMessage) {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
    }

    public static DomainResult Success() => new(true, null);

    public static DomainResult Failure(string message) =>
        throw new InvalidOperationException(message);
}