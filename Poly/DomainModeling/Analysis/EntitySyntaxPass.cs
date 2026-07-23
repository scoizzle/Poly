using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Analysis pass that produces <see cref="EntitySyntaxMetadata"/> —
/// the TypeDefinitionNode trees representing entity types, stage enums,
/// DomainResult infrastructure, and lowered policies.
///
/// This pass runs during domain analysis so the Syntax metadata is available
/// in <see cref="AnalysisResult"/> for any downstream consumer (generators,
/// MCP export tools, target packs).
/// </summary>
internal sealed class EntitySyntaxPass : INodeAnalyzer {
    public string PassName => "EntitySyntaxPass";
    public string[] Dependencies => [];

    public void Analyze(AnalysisContext context, Node node) {
        if (node is not Domain domain) return;
        if (context.HasStructuralFailure) return;

        try {
            var types = Lowering.DomainProgramProjection.ToSyntax(domain, context);
            context.SetMetadata(domain, new EntitySyntaxMetadata(types));
        }
        catch (Exception ex) {
            // Projection failed — likely due to partial/immature domain state.
            // Metadata simply won't be available; consumers check for null.
            context.ReportDiagnostic(domain,
                Poly.Syntax.Analysis.DiagnosticSeverity.Warning,
                $"Entity syntax projection failed: {ex.Message}");
        }
    }
}