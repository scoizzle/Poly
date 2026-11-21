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
}