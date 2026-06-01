using System.Buffers;

using Poly.Syntax.Analysis;

namespace Poly.Interpretation.TreeWalking;

/// <summary>
/// Configuration options for the tree-walking interpreter.
/// </summary>
public sealed record InterpreterOptions {
    public static InterpreterOptions Default { get; } = new();

    /// <summary>
    /// Maximum allowed depth of the call stack before throwing a stack overflow error.
    /// </summary>
    public int MaxStackDepth { get; init; } = 1000;

    /// <summary>
    /// Initial capacity for the evaluation stack.
    /// </summary>
    public int InitialStackCapacity { get; init; } = 64;

    /// <summary>
    /// Whether to enable additional runtime checks (slower but safer).
    /// </summary>
    public bool StrictMode { get; init; } = false;

    /// <summary>
    /// If true, the interpreter will automatically suspend at certain semantic points
    /// (stage transitions, event boundaries, etc.) to allow insight analysis.
    /// </summary>
    public bool AutoSuspendForAnalysis { get; init; } = true;
}