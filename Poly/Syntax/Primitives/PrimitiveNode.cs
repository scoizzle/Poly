namespace Poly.Syntax.Primitives;

/// <summary>
/// Abstract base record for all primitive nodes.
/// Primitives are the irreducible instruction set that structured AST nodes
/// decompose to via <see cref="Node.ToPrimitives(AnalysisContext)"/>.
/// Each primitive declares its stack effect — how many values it pops and pushes.
/// </summary>
public abstract record PrimitiveNode : Node {
    /// <summary>
    /// Stack effect of this primitive: (PopCount, PushCount).
    /// Used by the ring allocator in ProgramCompiler to compute register slots.
    /// This is the canonical source — no external switch or metadata needed.
    /// </summary>
    public abstract (int Pop, int Push) StackEffect { get; }

    /// <summary>
    /// Primitives cannot be expanded further — they are the terminal representation.
    /// </summary>
    public sealed override IEnumerable<PrimitiveNode> ToPrimitives(AnalysisContext context)
        => [this];
}