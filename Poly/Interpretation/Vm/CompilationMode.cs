namespace Poly.Interpretation.Vm;

/// <summary>
/// Controls the level of debug/tracing instrumentation in the compiled delegate.
/// </summary>
public enum CompilationMode {
    /// <summary>Full debug/tracing support (default). Includes DebugInterrupt checks
    /// and AST node tracking for symbolic debug position.</summary>
    Normal,

    /// <summary>No debug instrumentation. Omit DebugInterrupt checks for maximum
    /// execution speed. Suitable for production or benchmarks.</summary>
    NoDebug,
}