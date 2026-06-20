using System.Linq.Expressions;

namespace Poly.Interpretation.Vm.Instructions;

/// <summary>
/// Assembly-only marker: signals that the next instruction which pops from the
/// eval stack needs φ.  The ring buffer at this point carries the "primary"
/// µop PCs (then/true branch).  This marker carries the "alt" PCs (else/false
/// branch) and the source µop PC that identifies the alt path.
/// Generates no runtime code (<see cref="ToExpression"/> returns null).
/// </summary>
public sealed record PhiMarker(int[] AltPcs, int SourcePc, NodeId? AstSource = null) : Instruction(AstSource) {
    public override int PopCount => 0;
    public override int PushCount => 0;
    public override Expression? ToExpression(CompilationContext ctx) => null;
}