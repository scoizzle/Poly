namespace Poly.Tests.TestHelpers;

/// <summary>The process-wide cap on live children, whatever their kind (polyc, MCP host, child test host, oracle
/// app). Every spawn path acquires before it starts and releases only after the child is killed and waited for.
/// <see cref="PackOracle"/> waits on <see cref="Slots"/> directly, so both draw from the same two slots.
/// The semaphore is the only count: an earlier <c>LiveChildren</c>/<c>PeakLiveChildren</c> pair was read by nothing
/// (and could not count the oracle's direct holders), so it was deleted rather than kept as a second truth.</summary>
internal static class ChildProcessGate {
    public const int Capacity = 2;
    public static readonly TimeSpan SlotWait = TimeSpan.FromSeconds(30);
    public static SemaphoreSlim Slots { get; } = new(Capacity, Capacity);

    /// <summary>Waits for a free slot, at most <paramref name="wait"/> (default <see cref="SlotWait"/>).
    /// A caller-requested cancel surfaces as <see cref="OperationCanceledException"/>; only the bounded wait running
    /// out is the named <see cref="ChildProcessSlotTimeoutException"/>.</summary>
    public static async Task AcquireAsync(CancellationToken ct, TimeSpan? wait = null) {
        var limit = wait ?? SlotWait;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(limit);
        try {
            await Slots.WaitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
            throw new ChildProcessSlotTimeoutException(
                $"No child slot free within {limit.TotalSeconds:0.###} s (at most {Capacity} live children).");
        }
    }

    /// <summary>Returns a slot taken with <see cref="AcquireAsync"/>.</summary>
    public static void Release() => Slots.Release();
}

internal sealed class ChildProcessSlotTimeoutException(string message) : TimeoutException(message);