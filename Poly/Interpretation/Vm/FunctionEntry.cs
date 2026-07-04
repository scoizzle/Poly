namespace Poly.Interpretation.Vm;

/// <summary>Metadata for a callable function within a compiled program.
/// EmitPrimitiveCall reads <see cref="LocalCount"/> and <see cref="PC"/>
/// at compile time to inline the call frame setup.</summary>
public sealed record FunctionEntry(int PC, int ArgSlots, int LocalCount = 0);