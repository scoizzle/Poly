using Poly.DomainModeling.Lowering;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Analysis pass that produces <see cref="TransportMetadata"/> —
/// protocol-level resource hierarchy and exposability.
///
/// Wraps <see cref="TransportAnalyzer"/> as a pass, consuming
/// <see cref="OwnershipAggregateMetadata"/> and <see cref="EffectTopologyMetadata"/>.
/// Depends on <see cref="OwnershipAggregatePass"/> and <see cref="EffectTopologyPass"/>.
/// </summary>
internal sealed class TransportPass : INodeAnalyzer {
    public const string Id = "TransportPass";
    public string PassName => Id;
    public string[] Dependencies => [EffectTopologyPass.Id, OwnershipAggregatePass.Id];

    public void Analyze(AnalysisContext context, Node node) {
        if (node is not Domain domain) return;
        if (context.HasStructuralFailure) return;

        var topology = context.GetMetadata<EffectTopologyMetadata>(domain)?.Topology;
        var aggregate = context.GetMetadata<OwnershipAggregateMetadata>(domain)?.Aggregate;

        // Issue 17: Fail closed when metadata is missing
        if (aggregate == null || topology == null) {
            context.ReportDiagnostic(domain,
                DiagnosticSeverity.Error,
                "TransportPass requires EffectTopologyMetadata and OwnershipAggregateMetadata. " +
                "Ensure EffectTopologyPass and OwnershipAggregatePass run before TransportPass.",
                code: "TransportPass.MissingDependency");
            return;
        }

        var analyzer = new TransportAnalyzer(domain);
        var transport = analyzer.Analyze(aggregate, topology);
        context.SetMetadata(domain, new TransportMetadata(transport));
    }
}