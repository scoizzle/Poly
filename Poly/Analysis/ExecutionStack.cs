using System.Runtime.CompilerServices;

namespace Poly.Analysis;

/// <summary>
/// A stack overflow cannot be caught, so a recursive walk over an expression tree probes the calling thread's
/// remaining stack before it descends again, and turns "not enough stack" into a typed failure (#882). The parse
/// limit (<c>DslLimits.MaxExpressionDepth</c>, 256 levels) is a counted depth, not a stack guarantee: the compiler
/// doors run their passes on the 16 MB parse stack for that reason, and a caller that walks a pass on a thread too
/// small for the document gets this exception instead of the process aborting.
/// </summary>
internal static class ExecutionStack {
    /// <summary>
    /// The parse thread records where its stack started and the address below which the ABI compile must refuse
    /// (#1115). A legal invoke chain bounds the number of compiled callables but not the stack each link spends:
    /// an invoke inside nested branches is thousands of frames deep, so the emitter needs a byte budget, not a
    /// callable count. Zero off a parse thread, where only the remaining-stack probe is available.
    /// </summary>
    [ThreadStatic] private static long _stackBase;
    [ThreadStatic] private static long _stackCeiling;

    /// <summary>
    /// How much of a declared stack the compile may spend before it refuses: three quarters, leaving a quarter for
    /// the work still unchecked below the last probe and for unwinding the refusal. A legal invoke chain deep enough
    /// to reach it is a typed failure, never an abort.
    /// </summary>
    private const long StackBudgetNumerator = 3;
    private const long StackBudgetDenominator = 4;

    /// <summary>
    /// Records the calling thread's stack base and refuses the ABI compile past the budget measured from it.
    /// Called once by <c>DslLimits.RunOnParseStack</c> as the parse thread starts.
    /// </summary>
    public static void DeclareStack(int stackBytes) {
        _stackBase = CurrentStackAddress();
        _stackCeiling = _stackBase - (long)stackBytes * StackBudgetNumerator / StackBudgetDenominator;
    }

    /// <summary>
    /// Throws <see cref="InsufficientExecutionStackException"/> when the calling thread has too little stack left
    /// for one more level. Callers that report failures turn it into a refusal; nothing turns a stack overflow into
    /// anything, so every unbounded walk calls this before it descends.
    /// </summary>
    public static void Ensure() {
        if (RuntimeHelpers.TryEnsureSufficientExecutionStack())
            return;
        throw new InsufficientExecutionStackException(
            "The document's expression tree is deeper than the calling thread's remaining stack. " +
            "Nothing was produced; run the compilation on a thread with a larger stack.");
    }

    /// <summary>
    /// The ABI compile's recursion door: every node the emitter visits calls this before it descends. On a declared
    /// stack it refuses once the compile has spent the budget, which is what bounds an invoke chain whose stack use
    /// per link is not counted. Off a declared stack it falls back to the remaining-stack probe.
    /// </summary>
    public static void EnsureCompileFrame() {
        if (_stackBase == 0) {
            Ensure();
            return;
        }
        if (CurrentStackAddress() > _stackCeiling)
            return;
        throw new InsufficientExecutionStackException(
            "The compile has spent the parse thread's stack budget on a deeply nested expression or invoke chain. " +
            "Nothing was produced; shorten the chain.");
    }

    /// <summary>A stack address at the probe's own depth. Only differences are used, so the exact frame is irrelevant.</summary>
    private static unsafe long CurrentStackAddress() {
        byte marker = 0;
        return (long)(nint)(&marker);
    }
}