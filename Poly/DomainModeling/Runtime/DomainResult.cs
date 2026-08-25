namespace Poly.DomainModeling.Runtime;

/// <summary>
/// Shared <c>DomainResult</c> tree for VM and generated C#.
/// <see cref="Success"/> / <see cref="Failure"/> return an object with
/// <see cref="IsSuccess"/>; they do not throw. Canonical fail-closed shape:
/// <c>if (!result.IsSuccess) return result</c> is live on both paths;
/// <c>ExecuteEffect</c> throws when a VM program returns a failed result
/// (foreach zero-match, per-item invoke Failure from <c>InvokeNamed</c>).
/// Missing actions still throw from <c>InvokeNamed</c> (not a result object).
/// </summary>
public sealed class DomainResult {
    public bool IsSuccess { get; }
    public string? ErrorMessage { get; }
    public object? Value { get; }

    private DomainResult(bool isSuccess, string? errorMessage, object? value) {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        Value = value;
    }

    public static DomainResult Success(object? value = null) => new(true, null, value);

    public static DomainResult Failure(string message) => new(false, message, null);
}