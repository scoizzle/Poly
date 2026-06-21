using System.Linq.Expressions;
using System.Reflection;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm.Instructions;

/// <summary>Count set bits (popcount) in a <c>long</c> value using
/// <c>System.Numerics.BitOperations.PopCount()</c>.</summary>
public sealed record CountBits(string? Alias = null, NodeId? AstSource = null) : Instruction(AstSource) {
    public override int PopCount => 1;
    public override int PushCount => 1;

    internal static readonly MethodInfo PopCountMethod =
        typeof(System.Numerics.BitOperations).GetMethod(nameof(System.Numerics.BitOperations.PopCount),
            [typeof(ulong)])!;

    public override Expression? ToExpression(CompilationContext ctx) {
        var value = ctx.ResolveValue(this, 0);
        var result = Call(null, PopCountMethod, Convert(value, typeof(ulong)));
        return Assign(ctx.ValueSlot(ctx.CurrentLabelIndex), Convert(result, typeof(long)));
    }
}