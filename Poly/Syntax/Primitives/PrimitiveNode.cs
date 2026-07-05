namespace Poly.Syntax.Primitives;

/// <summary>
/// Abstract base record for all primitive nodes.
/// Primitives are the irreducible instruction set that structured AST nodes
/// decompose to via <see cref="Node.ToPrimitives(ExpansionContext)"/>.
/// Each primitive declares its stack effect — how many values it pops and pushes.
///
/// Primitives MAY also carry explicit dataflow information via
/// <see cref="InputSlots"/> and <see cref="ResultSlot"/>. When present,
/// the compiler uses these edges directly instead of simulating the
/// evaluation stack via <c>StackEffect</c>.
/// </summary>
public abstract record PrimitiveNode : Node {
    /// <summary>
    /// Stack effect of this primitive: (PopCount, PushCount).
    /// Used by the ring allocator in ProgramCompiler to compute register slots.
    /// This is the canonical source — no external switch or metadata needed.
    /// When <see cref="InputSlots"/> and <see cref="ResultSlot"/> are provided,
    /// the stack effect is derived from those instead.
    /// </summary>
    public abstract (int Pop, int Push) StackEffect { get; }

    /// <summary>
    /// Primitives cannot be expanded further — they are the terminal representation.
    /// </summary>
    public sealed override IEnumerable<PrimitiveNode> ToPrimitives(Primitives.ExpansionContext context)
        => [this];
}