namespace Poly.Data.Modeling.Analysis;

public enum DomainModelDiagnosticSeverity {
    Info,
    Warning,
    Error
}

public sealed record DomainModelDiagnostic(
    DomainModelDiagnosticSeverity Severity,
    string Code,
    string Message,
    string? Location = null);