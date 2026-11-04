namespace Poly.Interpretation.Analysis.Semantics;

/// <summary>
/// Element reads that stayed available across one array element write because the
/// available-expression pass proved the keys differ. Set on the analyzed root. It
/// records what the pass proved; it rewrites nothing and no local pass reads it.
/// </summary>
/// <param name="Loans">One entry per write that kept at least one element read.</param>
public sealed record PlaceLoanMetadata(IReadOnlyList<PlaceLoan> Loans) : IAnalysisMetadata;

/// <summary>An element write and the element reads of the same array it did not kill.</summary>
/// <param name="Array">The array variable's declaration.</param>
/// <param name="Write">The written element access.</param>
/// <param name="Preserved">First occurrence of each element read the write left available.</param>
public sealed record PlaceLoan(Variable Array, IndexAccess Write, IReadOnlyList<Node> Preserved);