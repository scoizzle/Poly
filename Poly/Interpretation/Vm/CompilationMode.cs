namespace Poly.Interpretation.Vm;

/// <summary>
/// Controls the level of debug/tracing instrumentation in the compiled delegate.
/// </summary>
public enum CompilationMode {
    /// <summary>Full debug/tracing support (default). Includes DebugInterrupt checks,
    /// AST node tracking, and the <c>MaxLoopIterations</c> / <c>LoopTicks</c> sandbox.</summary>
    Normal,

    /// <summary>No debug instrumentation. Omit DebugInterrupt checks and loop-tick
    /// guards for maximum execution speed. Suitable for production or benchmarks.</summary>
    NoDebug,
}