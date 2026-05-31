namespace Poly.Syntax.Analysis;

/// <summary>
/// Controls how the analyzer pipeline behaves, particularly around early termination.
/// </summary>
public sealed record AnalysisOptions {
    /// <summary>
    /// Default options: run all analyzers to completion and collect as many diagnostics as possible.
    /// </summary>
    public static AnalysisOptions Default { get; } = new();

    /// <summary>
    /// Recommended options for evolution/feedback loops: structural errors can cause later expensive passes to be skipped for faster response.
    /// </summary>
    public static AnalysisOptions StopOnStructuralErrors { get; } = new() { Mode = AnalysisMode.StopOnStructuralErrors };

    /// <summary>
    /// The mode that determines early-exit behavior.
    /// </summary>
    public AnalysisMode Mode { get; init; } = AnalysisMode.Full;

    /// <summary>
    /// Whether the current options + state should allow skipping expensive passes after structural failures.
    /// </summary>
    internal bool ShouldStopOnStructuralErrors => Mode is AnalysisMode.StopOnStructuralErrors or AnalysisMode.FailFast;
}

/// <summary>
/// Defines how aggressively the analyzer pipeline should stop early on problems.
/// </summary>
public enum AnalysisMode {
    /// <summary>
    /// Always run every registered analyzer pass to completion.
    /// This is the default and produces the richest diagnostics.
    /// </summary>
    Full = 0,

    /// <summary>
    /// Structural and reference errors are allowed to cause later, more expensive analyzers to be skipped.
    /// Useful for fast feedback in evolution loops.
    /// </summary>
    StopOnStructuralErrors = 1,

    /// <summary>
    /// Stop analysis as soon as any error (of any kind) is reported.
    /// </summary>
    FailFast = 2,
}