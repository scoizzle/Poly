using Poly.Interpretation.Analysis.Semantics;

namespace Poly.Tests.TestHelpers;

/// <summary>The declarations an analysis registered (#998 T12b): the source a compile puts with its root in one
/// analysis, so every member body the root reaches is analyzed in the same result.</summary>
internal static class AnalysisContextDeclarations {
    public static AstTypeRegistry Declarations(this AnalysisContext context) =>
        context.GetMetadata<AstTypeRegistry>(default)
            ?? throw new InvalidOperationException("this analysis registered no declarations");
}