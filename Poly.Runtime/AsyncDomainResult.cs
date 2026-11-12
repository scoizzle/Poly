namespace Poly.Runtime;

/// <summary>
/// The completed-task factories a lowered <c>async</c> action returns.
/// <para>
/// Every factory returns an already-completed task. A failure travels as a value in a
/// successful task and is never a fault: the interpreter's <c>Await</c> blocks on the
/// operand with no scheduler, so an incomplete task would hang the simulator and a faulted
/// one would throw where Q3 forbids a domain failure to be an exception.
/// </para>
/// <para>
/// Results are <see cref="Task"/>/<see cref="Task{TResult}"/>. A
/// <c>ValueTask&lt;DomainResult&gt;</c> step is normalised at its caller with
/// <c>AsTask()</c>; this type ships no ValueTask-shaped factory, so the normalisation is a
/// documented rule, not code here.
/// </para>
/// </summary>
public static class AsyncDomainResult {
    /// <summary>A completed, successful void result.</summary>
    public static Task<DomainResult> Success() =>
        Task.FromResult(DomainResult.Success());

    /// <summary>A completed, failed void result carrying <paramref name="error"/> unchanged.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is null; thrown at the call site.</exception>
    public static Task<DomainResult> Failure(DomainFailure error) =>
        Task.FromResult(DomainResult.Failure(error));

    /// <summary>A completed, successful typed result carrying <paramref name="value"/>.</summary>
    public static Task<DomainResult<T>> Success<T>(T value) =>
        Task.FromResult(DomainResult<T>.Success(value));

    /// <summary>A completed, failed typed result carrying <paramref name="error"/> unchanged.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is null; thrown at the call site.</exception>
    public static Task<DomainResult<T>> Failure<T>(DomainFailure error) =>
        Task.FromResult(DomainResult<T>.Failure(error));
}