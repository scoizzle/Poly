using System.Collections.Generic;

namespace Poly.Interpretation.VirtualMachine;

internal static class Optimizer {
    public static Bytecode Optimize(Bytecode input) {
        var code = input.Code;
        var output = new List<byte>(code.Length);
        var pcMap = new Dictionary<int, int>(); // oldPC → newPC
        int i = 0;

        while (i < code.Length) {
            pcMap[i] = output.Count;

            if (TryFold(code, i, output, out int consumed)) {
                i += consumed;
                continue;
            }

            int len = InstructionLength(code, i);
            output.AddRange(code.AsSpan(i, len));
            i += len;
        }

        var patchedCode = PatchTargets([.. output], pcMap);
        var patchedRegions = PatchExceptionRegions(input.ExceptionRegions, pcMap);
        var sourceMap = input.SourceMap is Dictionary<int, NodeId> d ? d : new Dictionary<int, NodeId>(input.SourceMap);
        return new Bytecode(patchedCode, sourceMap, [.. input.Functions],
            [.. input.Constants], [.. input.CallSites], patchedRegions, input.ResultType);
    }

    private static bool TryFold(byte[] code, int i, List<byte> output, out int consumed) {
        consumed = 0;

        if (code.Length - i < 2) return false;

        var op1 = (OpCode)code[i];
        int len1 = InstructionLength(code, i);
        int next = i + len1;
        if (next >= code.Length) return false;

        var op2 = (OpCode)code[next];
        int val1 = len1 >= 5 ? ReadInt32(code, i + 1) : 0;

        // PushInt 0; Add/Sub/Dup → remove both (Add/Sub 0 is identity)
        if (op1 == OpCode.PushInt && val1 == 0 && op2 is OpCode.Add or OpCode.Sub or OpCode.Dup) {
            consumed = len1 + InstructionLength(code, next);
            return true;
        }

        // PushInt 1; Mul → remove both (Mul 1 is identity)
        if (op1 == OpCode.PushInt && val1 == 1 && op2 == OpCode.Mul) {
            consumed = len1 + InstructionLength(code, next);
            return true;
        }

        // Note: PushInt 0; Mul/Div are intentionally NOT folded.
        // These ops consume two values from the stack and the fold
        // requires tracking the second operand's value, which requires
        // stack depth tracking the peephole pass doesn't do.

        // Dup; Pop → remove both
        if (op1 == OpCode.Dup && op2 == OpCode.Pop) {
            consumed = InstructionLength(code, i) + InstructionLength(code, next);
            return true;
        }

        // Multi-Pop fold: Pop; Pop → Pop (keep one)
        if (op1 == OpCode.Pop && op2 == OpCode.Pop) {
            output.Add((byte)OpCode.Pop);
            consumed = InstructionLength(code, i) + InstructionLength(code, next);
            return true;
        }

        // PushInt 0; Neg → PushInt 0 (negating 0 is still 0)
        if (op1 == OpCode.PushInt && val1 == 0 && op2 == OpCode.Neg) {
            output.AddRange(code.AsSpan(i, len1));
            consumed = len1 + InstructionLength(code, next);
            return true;
        }

        // Not; Not is intentionally NOT folded. The VM's Not opcode is
        // logical not (val == 0 ? 1 : 0), not arithmetic negation.
        // Without knowing the value is boolean, the fold is unsound.

        return false;
    }

    private static int InstructionLength(byte[] code, int pc) {
        var op = (OpCode)code[pc];
        switch (op) {
            // 1-byte opcodes
            case OpCode.Nop:
            case OpCode.Dup:
            case OpCode.Pop:
            case OpCode.Not:
            case OpCode.Return:
            case OpCode.EndFinally:
            case OpCode.Throw:
            case OpCode.IsNull:
            case OpCode.CallClosure:
            case OpCode.LoadValue:
            case OpCode.StoreValue:
            case OpCode.Add:
            case OpCode.Sub:
            case OpCode.Mul:
            case OpCode.Div:
            case OpCode.Mod:
            case OpCode.Neg:
            case OpCode.UDiv:
            case OpCode.UMod:
            case OpCode.Eq:
            case OpCode.Ne:
            case OpCode.Lt:
            case OpCode.Le:
            case OpCode.Gt:
            case OpCode.Ge:
            case OpCode.ULt:
            case OpCode.ULe:
            case OpCode.UGt:
            case OpCode.UGe:
            case OpCode.DAdd:
            case OpCode.DSub:
            case OpCode.DMul:
            case OpCode.DDiv:
            case OpCode.DNeg:
            case OpCode.DEq:
            case OpCode.DNe:
            case OpCode.DLt:
            case OpCode.DLe:
            case OpCode.DGt:
            case OpCode.DGe:
                return 1;

            // 5-byte opcodes (1 + int32)
            case OpCode.PushInt:
            case OpCode.Narrow:
            case OpCode.Jump:
            case OpCode.JumpIfFalse:
            case OpCode.Call:
            case OpCode.CallExternal:
            case OpCode.StoreArg:
            case OpCode.LoadArg:
            case OpCode.LoadLocal:
            case OpCode.StoreLocal:
            case OpCode.LoadConst:
            case OpCode.Int:
            case OpCode.Iret:
            case OpCode.LoadUpvalue:
            case OpCode.StoreUpvalue:
                return 5;

            // 9-byte opcodes (1 + int64)
            case OpCode.PushLong:
            case OpCode.PushDouble:
                return 9;

            // 9-byte opcode (1 + int32 + int32)
            case OpCode.AllocateClosure:
                return 9;

            default:
                return 1;
        }
    }

    private static int ReadInt32(byte[] code, int pc) =>
        code[pc] | (code[pc + 1] << 8) | (code[pc + 2] << 16) | (code[pc + 3] << 24);

    private static byte[] PatchTargets(byte[] code, Dictionary<int, int> pcMap) {
        int i = 0;
        while (i < code.Length) {
            var op = (OpCode)code[i];
            int len = InstructionLength(code, i);
            if (op is OpCode.Jump or OpCode.JumpIfFalse or OpCode.Call) {
                int oldTarget = ReadInt32(code, i + 1);
                if (pcMap.TryGetValue(oldTarget, out int newTarget))
                    WriteInt32(code, i + 1, newTarget);
                else if (oldTarget < code.Length)
                    WriteInt32(code, i + 1, oldTarget); // keep as-is if not mapped (boundary case)
            }
            i += len;
        }
        return code;
    }

    private static List<ExceptionRegion> PatchExceptionRegions(
        IReadOnlyList<ExceptionRegion> regions, Dictionary<int, int> pcMap) {
        var result = new List<ExceptionRegion>(regions.Count);
        foreach (var r in regions) {
            result.Add(new ExceptionRegion(
                Remap(r.TryStart, pcMap),
                Remap(r.TryEnd, pcMap),
                r.CatchStart >= 0 ? Remap(r.CatchStart, pcMap) : -1,
                r.FinallyStart is not null ? Remap(r.FinallyStart.Value, pcMap) : null));
        }
        return result;
    }

    private static int Remap(int oldPc, Dictionary<int, int> pcMap) =>
        pcMap.TryGetValue(oldPc, out int newPc) ? newPc : oldPc;

    private static void WriteInt32(byte[] code, int offset, int value) {
        code[offset] = (byte)(value & 0xFF);
        code[offset + 1] = (byte)((value >> 8) & 0xFF);
        code[offset + 2] = (byte)((value >> 16) & 0xFF);
        code[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}