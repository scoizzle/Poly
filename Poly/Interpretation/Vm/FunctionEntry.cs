namespace Poly.Interpretation.Vm;

/// <summary>Metadata for a callable function within a compiled program.
/// Used by HandleCall to dispatch to the correct µop range.</summary>
public sealed record FunctionEntry(int PC, int ArgSlots, int LocalCount = 0);