namespace Poly.Interpretation.Analysis;

/// <summary>
/// Represents a diagnostic message (error, warning, information, or hint) produced during analysis.
/// </summary>
/// <param name="Node">The AST node associated with this diagnostic.</param>
/// <param name="Severity">The severity level of the diagnostic.</param>
/// <param name="Message">Human-readable diagnostic message.</param>
/// <param name="Code">Optional diagnostic code for categorization and suppression.</param>
public sealed record Diagnostic(
    Node Node,
    DiagnosticSeverity Severity,
    string Message,
    string? Code = null
);

/// <summary>
/// Severity level for diagnostics.
/// </summary>
public enum DiagnosticSeverity {
    /// <summary>
    /// A critical error that prevents compilation or execution.
    /// </summary>
    Error,

    /// <summary>
    /// A warning about potentially problematic code.
    /// </summary>
    Warning,

    /// <summary>
    /// Informational message about the code.
    /// </summary>
    Information,

    /// <summary>
    /// A hint or suggestion for code improvement.
    /// </summary>
    Hint
}


public static class DiagnosticExtensions {
    extension(AnalysisContext context) {
        /// <summary>
        /// Reports an error diagnostic for the specified node.
        /// </summary>
        public void ReportError(Node node, string message, string? code = null)
            => context.ReportDiagnostic(node, DiagnosticSeverity.Error, message, code);

        /// <summary>
        /// Reports a warning diagnostic for the specified node.
        /// </summary>
        public void ReportWarning(Node node, string message, string? code = null)
            => context.ReportDiagnostic(node, DiagnosticSeverity.Warning, message, code);

        /// <summary>
        /// Reports an information diagnostic for the specified node.
        /// </summary>
        public void ReportInformation(Node node, string message, string? code = null)
            => context.ReportDiagnostic(node, DiagnosticSeverity.Information, message, code);

        /// <summary>
        /// Reports a hint diagnostic for the specified node.
        /// </summary>
        public void ReportHint(Node node, string message, string? code = null)
            => context.ReportDiagnostic(node, DiagnosticSeverity.Hint, message, code);
    }
}