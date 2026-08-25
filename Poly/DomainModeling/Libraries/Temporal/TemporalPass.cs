using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Libraries.Temporal;

/// <summary>
/// Vocabulary bag: this unit loaded <c>uses temporal</c>. Checks, elaboration, and
/// lowering consume analysis — not a session Meaning table.
/// </summary>
public sealed record TemporalVocabularyMetadata : IAnalysisMetadata;

/// <summary>Registers temporal vocabulary on the domain when the library is loaded.</summary>
public sealed class TemporalPass : INodeAnalyzer {
    public const string Id = "Temporal";
    public string PassName => Id;

    public void Analyze(AnalysisContext context, Node node) {
        if (node is Domain domain)
            context.SetMetadata(domain, new TemporalVocabularyMetadata());
    }
}