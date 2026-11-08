using Poly.DomainModeling.Contradictions;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// The contradiction bag for a domain. Published by the <c>contradictions</c> library's pass
/// (an empty bag whenever the library is loaded); read as
/// <c>analysis.GetMetadata&lt;ContradictionMetadata&gt;(domain)</c>.
/// </summary>
public sealed record ContradictionMetadata(ContradictionBag Bag) : IAnalysisMetadata;