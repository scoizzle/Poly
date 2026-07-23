using Poly.DomainModeling.Lowering;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Analysis pass that produces <see cref="EffectTopologyMetadata"/> —
/// cross-entity effect coupling (create-in, cross-entity invoke, subscriptions).
///
/// Wraps <see cref="EffectTopologyAnalyzer.Scan"/> as a pass so the topology
/// metadata is available on <see cref="AnalysisResult"/> for downstream consumers.
/// </summary>
internal sealed class EffectTopologyPass : INodeAnalyzer {
    public const string Id = "EffectTopologyPass";
    public string PassName => Id;
    public string[] Dependencies => [];

    public void Analyze(AnalysisContext context, Node node) {
        if (node is not Domain domain) return;
        if (context.HasStructuralFailure) return;

        var topology = EffectTopologyAnalyzer.Scan(domain);
        context.SetMetadata(domain, new EffectTopologyMetadata(topology));
    }
}