namespace Poly.Data.Modeling;

internal sealed record PropertyValidationAstMetadata(Node ValidationAst) : IAnalysisMetadata;
internal sealed record TransitionValidationAstMetadata(Node TransitionGuardAst) : IAnalysisMetadata;
internal sealed record ActionCoverageMetadata(IReadOnlySet<Property> CoveredProperties) : IAnalysisMetadata;