namespace Poly.Interpretation.Vm;

/// <summary>
/// Maps µop PC to ring depth at that point in execution.
/// Used by debugger, EH dispatch, and stack trace reconstruction
/// to determine the logical eval-stack depth at any PC.
///
/// This is a compile-time artifact that bridges the gap between
/// the ring allocation model (values flow through _r{k} registers)
/// and runtime stack inspection (which needs logical depth).
///
/// See K-035 (ghost ValueStack) and open Q9 in the architecture review.
/// </summary>
/// <param name="DepthAtPC">Dictionary mapping µop PC → ring depth (eval-stack item count).</param>
public sealed record PcToRingDepth(
    IReadOnlyDictionary<int, int> DepthAtPC
);