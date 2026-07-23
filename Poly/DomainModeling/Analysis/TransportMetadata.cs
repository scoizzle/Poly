using Poly.DomainModeling.Lowering;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Stores transport/resource hierarchy metadata: routing context, exposability.
/// Produced by <see cref="TransportPass"/>.
/// </summary>
public sealed record TransportMetadata(TransportSurface Transport) : IAnalysisMetadata;