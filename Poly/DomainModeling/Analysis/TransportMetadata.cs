using Poly.Analysis;
using Poly.DomainModeling.Lowering;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Stores transport/resource hierarchy metadata: routing context, exposability.
/// Produced by <see cref="TransportPass"/>.
/// </summary>
public sealed record TransportMetadata(TransportSurface Transport) : IAnalysisMetadata;