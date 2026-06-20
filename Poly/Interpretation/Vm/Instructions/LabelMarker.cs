using System.Linq.Expressions;

namespace Poly.Interpretation.Vm.Instructions;

/// <summary>
/// Assembly-only marker: records the flat position of a label ID.
/// Emitted by <c>UopGenerationPass</c> at every label target.
/// Generates no runtime code (<see cref="ToExpression"/> returns null).
/// </summary>
public sealed record LabelMarker(int LabelId, NodeId? AstSource = null) : Instruction(AstSource) {
    public override int PopCount => 0;
    public override int PushCount => 0;
    public override Expression? ToExpression(CompilationContext ctx) => null;
}