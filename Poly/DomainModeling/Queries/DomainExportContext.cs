using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Queries;

/// <summary>
/// What a document export reads besides the <see cref="Domain"/>: the <see cref="DomainSession"/> whose
/// print tables spell library expressions (temporal <c>Now</c>, <c>Today - 3 Days</c>) and the
/// <see cref="AnalysisResult"/> of the same compilation. A field-free record, so equality and
/// <c>with</c> copy only these three values; an exporter builds one printer per call from
/// <see cref="CreatePrinter"/>. With no <see cref="Session"/> the session is
/// <see cref="RuntimeAnalysisCache.Session"/> for the domain, resolved only when an expression is printed.
/// </summary>
/// <param name="Domain">The domain to export.</param>
/// <param name="Session">The session that prints expressions; null resolves it from the domain.</param>
/// <param name="Analysis">The analysis of the same compilation. Unread until D-04 (the published create rule).</param>
public sealed record DomainExportContext(
    Domain Domain,
    DomainSession? Session = null,
    AnalysisResult? Analysis = null) {
    internal DomainDslPrinter CreatePrinter() =>
        new(Session ?? RuntimeAnalysisCache.Session(Domain));
}
