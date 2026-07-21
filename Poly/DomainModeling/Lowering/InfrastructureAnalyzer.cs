using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Lowering;

/// <summary>
/// Coordinates <see cref="StorageAnalyzer"/> and <see cref="TransportAnalyzer"/>
/// to produce a unified <see cref="InfrastructureModel"/> for codegen backends.
///
/// Call <see cref="Analyze"/> to compute both storage and transport models from a domain.
/// When an <see cref="AnalysisResult"/> is available (from domain evolution), pass it to
/// leverage pre-computed metadata from the domain analysis pipeline.
/// </summary>
public sealed class InfrastructureAnalyzer {
    private readonly StorageAnalyzer _storage;
    private readonly TransportAnalyzer _transport;

    public InfrastructureAnalyzer(Domain domain, AnalysisResult? analysis = null) {
        _storage = new StorageAnalyzer(domain, analysis);
        _transport = new TransportAnalyzer(domain, analysis);
    }

    /// <summary>Computes the full infrastructure model for the domain.</summary>
    public InfrastructureModel Analyze() {
        // TransportAnalyzer must run first so EffectTopology is available
        // for StorageAnalyzer's parent resolution (create-in priority).
        var transport = _transport.Analyze();
        var storage = _storage.Analyze(transport.Effects);

        return new InfrastructureModel(
            storage.DomainName,
            storage,
            transport
        );
    }
}