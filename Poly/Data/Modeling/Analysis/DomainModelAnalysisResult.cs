namespace Poly.Data.Modeling.Analysis;

public sealed record DomainModelAnalysisResult(
    IReadOnlyList<DomainModelDiagnostic> Diagnostics) {
    public bool IsValid => Diagnostics.All(diagnostic => diagnostic.Severity != DomainModelDiagnosticSeverity.Error);
}