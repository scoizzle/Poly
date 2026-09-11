namespace Poly.Analysis;

/// <summary>
/// Controls how the analyzer pipeline behaves, particularly around early termination.
/// </summary>
public sealed record AnalysisOptions {
    /// <summary>
    /// Default options: run all analyzers to completion and collect as many diagnostics as possible.
    /// </summary>
    public static AnalysisOptions Default { get; } = new();

    /// <summary>
    /// Skip later passes once any error-level diagnostic has been reported.
    /// Useful for fast feedback in evolution loops.
    /// </summary>
    public static AnalysisOptions FailFast { get; } = new() { Mode = AnalysisMode.FailFast };

    /// <summary>
    /// The mode that determines early-exit behavior.
    /// </summary>
    public AnalysisMode Mode { get; init; } = AnalysisMode.Full;

    /// <summary>
    /// Whether the current options should skip later passes once <see cref="AnalysisContext.HasErrors"/> is true.
    /// </summary>
    internal bool ShouldStopOnErrors => Mode == AnalysisMode.FailFast;
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
    /// Skip later passes once any error-level diagnostic has been reported.
    /// </summary>
    FailFast = 1,
}
