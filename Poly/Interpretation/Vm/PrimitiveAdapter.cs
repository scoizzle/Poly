using Poly.Interpretation.Vm.Instructions;

using Prim = Poly.Syntax.Primitives;
using PrimOpKind = Poly.Syntax.Primitives.OpKind;
using PrimUnaryOpKind = Poly.Syntax.Primitives.UnaryOpKind;
using VmBinOp = Poly.Interpretation.Vm.Instructions.BinOp;
using VmCall = Poly.Interpretation.Vm.Instructions.Call;
using VmUnaryOp = Poly.Interpretation.Vm.Instructions.UnaryOp;
using VmUnaryOpKind = Poly.Interpretation.Vm.Instructions.UnaryOpKind;

namespace Poly.Interpretation.Vm;

/// <summary>
/// Converts a linked (label-resolved) <see cref="PrimitiveNode"/> sequence
/// into a <see cref="LoweringResult"/> consumable by <see cref="ProgramCompiler.Compile"/>.
///
/// This is a direct flat mapping — no block ordering, no dominator tree, no
/// ConsumedFromPcs computation (the existing BackwardScan handles that).
/// </summary>
internal static class PrimitiveAdapter {
    /// <summary>Converts linked primitives to a LoweringResult for ProgramCompiler.</summary>
    public static LoweringResult ToLoweringResult(IReadOnlyList<Prim.PrimitiveNode> primitives) {
        var instructions = new List<Instruction>(primitives.Count);

        foreach (var prim in primitives) {
            switch (prim) {
                case ResolvedGoto g:
                    instructions.Add(new Jump(g.TargetPc));
                    break;

                case ResolvedCondGoto cg:
                    instructions.Add(new BranchIfFalse(cg.TargetPc));
                    break;

                case Prim.Return:
                    instructions.Add(new ReturnOp());
                    break;

                case Prim.PushConstant pc:
                    instructions.Add(ConvertPushConstant(pc));
                    break;

                case Prim.LoadLocal ll:
                    instructions.Add(new LoadSlot(ll.SlotIndex));
                    break;

                case Prim.StoreLocal sl:
                    instructions.Add(new StoreSlot(sl.SlotIndex));
                    break;

                case Prim.BinaryOp bo:
                    instructions.Add(new VmBinOp(MapBinOpKind(bo.Op)));
                    break;

                case Prim.UnaryOp uo:
                    instructions.Add(new VmUnaryOp(MapUnaryOpKind(uo.Op)));
                    break;

                case Prim.Parameter p:
                    instructions.Add(new LoadSlot(p.SlotIndex));
                    break;

                case Prim.Discard:
                    instructions.Add(new PopOp());
                    break;

                case Prim.Dup:
                    instructions.Add(new DupOp());
                    break;

                case Prim.Throw:
                    instructions.Add(new Instructions.Throw());
                    break;

                case Prim.CountBits:
                    instructions.Add(new CountBits());
                    break;

                case Prim.ArrayLoad:
                    instructions.Add(new ArrayLoad());
                    break;

                case Prim.NewArray:
                    instructions.Add(new NewArrayOp());
                    break;

                case Prim.ArrayStore:
                    instructions.Add(new ArrayStore());
                    break;

                case Prim.StridedSet:
                    instructions.Add(new StridedSetOp());
                    break;

                case Prim.Call call:
                    instructions.Add(new VmCall(0, call.ArgCount + 1));
                    break;

                case Prim.Label:
                    // Labels are no-op markers; skip — they serve only as
                    // stable positions for branch targets in the primitive list.
                    // The µop list is parallel (same indices), so the slot stays.
                    instructions.Add(new Instructions.Nop());
                    break;

                default:
                    throw new NotSupportedException($"Unsupported primitive: {prim.GetType().Name}");
            }
        }

        return new LoweringResult(instructions);
    }

    private static Instruction ConvertPushConstant(Prim.PushConstant pc) {
        var val = pc.Value;
        if (val is long l)
            return new LoadConst(l);
        if (val is int i)
            return new LoadConst(i);
        if (val is string)
            return new LoadConst(0); // strings go through heap; placeholder
        if (val is bool b)
            return new LoadConst(b ? 1 : 0);
        if (val is null)
            return new LoadConst(0);
        if (val is double d)
            return new LoadConst((long)d);
        throw new NotSupportedException($"PushConstant type not supported: {val?.GetType()}");
    }

    private static BinOpKind MapBinOpKind(PrimOpKind op) => op switch {
        PrimOpKind.Add => BinOpKind.Add,
        PrimOpKind.Sub => BinOpKind.Sub,
        PrimOpKind.Mul => BinOpKind.Mul,
        PrimOpKind.Div => BinOpKind.Div,
        PrimOpKind.Mod => BinOpKind.Mod,
        PrimOpKind.And => BinOpKind.And,
        PrimOpKind.Or => BinOpKind.Or,
        PrimOpKind.Xor => BinOpKind.Xor,
        PrimOpKind.Shl => BinOpKind.Shl,
        PrimOpKind.Shr => BinOpKind.Shr,
        PrimOpKind.Eq => BinOpKind.Eq,
        PrimOpKind.Neq => BinOpKind.Ne,
        PrimOpKind.Gt => BinOpKind.Gt,
        PrimOpKind.Gte => BinOpKind.Ge,
        PrimOpKind.Lt => BinOpKind.Lt,
        PrimOpKind.Lte => BinOpKind.Le,
        _ => throw new ArgumentOutOfRangeException(nameof(op))
    };

    private static VmUnaryOpKind MapUnaryOpKind(PrimUnaryOpKind op) => op switch {
        PrimUnaryOpKind.Neg => VmUnaryOpKind.Neg,
        PrimUnaryOpKind.Not => VmUnaryOpKind.Not,
        PrimUnaryOpKind.BitNot => VmUnaryOpKind.BitNot,
        _ => throw new ArgumentOutOfRangeException(nameof(op))
    };
}