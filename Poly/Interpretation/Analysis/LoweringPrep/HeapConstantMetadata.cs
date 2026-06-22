using Poly.Syntax.Analysis;

namespace Poly.Interpretation.Analysis.LoweringPrep;

/// <summary>
/// Holds heap-allocated constant values collected during UopGeneration.
/// These are non-numeric, non-boolean constants (strings, CLR objects, etc.)
/// that must be pre-loaded into the VM's heap before execution.
/// The index into <see cref="Values"/> is the heap handle emitted in
/// <c>LoadHeapConst</c> instructions.
/// </summary>
/// <param name="Values">The accumulated heap constant values.</param>
public sealed record HeapConstantMetadata(List<object?> Values) : IAnalysisMetadata;