using Poly.DomainModeling.Lowering;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Analysis pass that produces <see cref="BehaviorMetadata"/> —
/// per-entity action metadata (parameters, return types, effective policies, stage transitions).
///
/// Wraps <see cref="BehaviorAnalyzer"/> as a pass.
/// Depends on <see cref="SemanticDomainAnalyzer"/> for type resolution and
/// <see cref="CapabilityAnalyzer"/> for action capability views.
/// </summary>
internal sealed class BehaviorPass : INodeAnalyzer {
    public const string Id = "BehaviorPass";
    public string PassName => Id;
    public string[] Dependencies => [SemanticDomainAnalyzer.Id, CapabilityAnalyzer.Id];

    public void Analyze(AnalysisContext context, Node node) {
        if (node is not Domain domain) return;
        if (context.HasStructuralFailure) return;

        var analyzer = new BehaviorAnalyzer(domain, context: context);
        var behavior = analyzer.Analyze();
        context.SetMetadata(domain, new BehaviorMetadata(behavior));
    }
}