namespace Poly.Interpretation.Vm;

/// <summary>Metadata for a callable function within a compiled VM program.
/// The <see cref="DirectVmAbiEmitter"/> reads <see cref="LocalCount"/> and
/// <see cref="PC"/> at compile time to inline the call frame setup (argument
/// placement, frame header push, and jump to function body).</summary>
/// <param name="PC">Start offset (program counter) of the function body
/// within the compiled delegate.</param>
/// <param name="ArgSlots">Number of argument slots consumed by this function.
/// Includes the instance slot for instance methods.</param>
/// <param name="LocalCount">Number of local variable slots required by this
/// function. Defaults to 0.</param>
public sealed record FunctionEntry(int PC, int ArgSlots, int LocalCount = 0);