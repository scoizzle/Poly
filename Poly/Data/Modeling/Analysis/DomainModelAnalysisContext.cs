namespace Poly.Data.Modeling.Analysis;

public sealed class DomainModelAnalysisContext {
    private readonly List<DomainModelDiagnostic> _diagnostics = [];

    public IReadOnlyList<DomainModelDiagnostic> Diagnostics => _diagnostics;

    public void ReportError(string code, string message, string? location = null) {
        _diagnostics.Add(new DomainModelDiagnostic(DomainModelDiagnosticSeverity.Error, code, message, location));
    }

    public void ReportWarning(string code, string message, string? location = null) {
        _diagnostics.Add(new DomainModelDiagnostic(DomainModelDiagnosticSeverity.Warning, code, message, location));
    }

    public void ReportInfo(string code, string message, string? location = null) {
        _diagnostics.Add(new DomainModelDiagnostic(DomainModelDiagnosticSeverity.Info, code, message, location));
    }
}