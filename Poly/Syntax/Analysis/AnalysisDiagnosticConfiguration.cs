namespace Poly.Syntax.Analysis;

public enum AnalysisDiagnosticVerbosity {
    All,
    InformationAndAbove,
    WarningAndAbove,
    ErrorOnly
}

public sealed record AnalysisDiagnosticConfiguration {
    public static AnalysisDiagnosticConfiguration Default { get; } = new();

    public bool TreatWarningsAsErrors { get; init; }
    public AnalysisDiagnosticVerbosity Verbosity { get; init; } = AnalysisDiagnosticVerbosity.All;

    public DiagnosticSeverity NormalizeSeverity(DiagnosticSeverity severity) {
        return TreatWarningsAsErrors && severity == DiagnosticSeverity.Warning
            ? DiagnosticSeverity.Error
            : severity;
    }

    public bool ShouldInclude(DiagnosticSeverity severity) {
        var normalized = NormalizeSeverity(severity);

        return Verbosity switch {
            AnalysisDiagnosticVerbosity.All => true,
            AnalysisDiagnosticVerbosity.InformationAndAbove => normalized is not DiagnosticSeverity.Hint,
            AnalysisDiagnosticVerbosity.WarningAndAbove => normalized is DiagnosticSeverity.Warning or DiagnosticSeverity.Error,
            AnalysisDiagnosticVerbosity.ErrorOnly => normalized == DiagnosticSeverity.Error,
            _ => true
        };
    }
}