using Poly.Syntax.Analysis;

namespace Poly.Interpretation.VirtualMachine;

internal sealed class BytecodeBuilder {
    private readonly List<byte> _code = [];
    private readonly Dictionary<string, int> _labels = [];
    private readonly List<(int offset, string label)> _pending = [];
    private int _labelCounter;

    public int Offset => _code.Count;
    public string NextLabel() => $"L{_labelCounter++}";

    public static int EstimateMaxStack(byte[] code) {
        // Conservative: walk linearly, track SP per instruction.
        // Skip unreachable code after unconditional Jump/Return.
        int sp = 0, max = 0;
        bool reachable = true;
        for (int i = 0; i < code.Length && reachable;) {
            byte raw = code[i];
            int size = (raw & 0x40) != 0 ? 9 : 1;
            var op = (OpCode)(raw & 0x3F);
            switch (op) {
                case OpCode.Push or OpCode.IncLocal or OpCode.Dup
                    or OpCode.LoadLocal or OpCode.LoadArg or OpCode.LoadValue or OpCode.LoadUpvalue:
                    sp++; break;
                case OpCode.Pop or OpCode.StoreLocal or OpCode.StoreArg or OpCode.StoreValue or OpCode.StoreUpvalue:
                    sp--; break;
                case OpCode.Add or OpCode.Sub or OpCode.Mul or OpCode.Div or OpCode.DivRem
                    or OpCode.Eq or OpCode.Ne or OpCode.Lt or OpCode.Le or OpCode.Gt or OpCode.Ge
                    or OpCode.BitAnd or OpCode.BitOr or OpCode.BitXor or OpCode.Shl or OpCode.Shr:
                    sp--; break;
                case OpCode.JumpIfFalse:
                    sp--;
                    // fall-through path stays reachable; jump path tracked via label analysis (TODO)
                    break;
                case OpCode.Jump:
                case OpCode.Return:
                    reachable = false; break; // skip unreachable
                case OpCode.CallClosure:
                    sp--; break;
            }
            if (sp > max) max = sp;
            i += size;
        }
        return max < 16 ? 16 : max; // minimum 16 to handle entry frames
    }

    public void Emit(OpCode op) {
        _code.Add((byte)op);
    }

    public void Emit(OpCode op, long operand) {
        _code.Add((byte)((byte)op | OpCodeEncoding.SizeBit));
        _code.Add((byte)(operand & 0xFF));
        _code.Add((byte)((operand >> 8) & 0xFF));
        _code.Add((byte)((operand >> 16) & 0xFF));
        _code.Add((byte)((operand >> 24) & 0xFF));
        _code.Add((byte)((operand >> 32) & 0xFF));
        _code.Add((byte)((operand >> 40) & 0xFF));
        _code.Add((byte)((operand >> 48) & 0xFF));
        _code.Add((byte)((operand >> 56) & 0xFF));
    }

    public void Emit(OpCode op, int operand) => Emit(op, (long)operand);

    public void Mark(string label) {
        _labels[label] = Offset;
    }

    public void EmitJump(OpCode op, string target) {
        int patchOffset = Offset;
        Emit(op, 0L);
        _pending.Add((patchOffset, target));
    }

    public byte[] Build() {
        foreach (var (patchOffset, label) in _pending) {
            if (!_labels.TryGetValue(label, out int targetOffset))
                throw new InvalidOperationException($"Undefined label: '{label}'");

            byte[] operand = BitConverter.GetBytes((long)targetOffset);
            for (int i = 0; i < 8; i++)
                _code[patchOffset + 1 + i] = operand[i];
        }

        return [.. _code];
    }

    public Bytecode BuildProgram(
        List<FunctionEntry>? functions = null,
        List<object?>? constants = null,
        List<CallSiteDelegate>? callSites = null,
        List<ExceptionRegion>? exceptionRegions = null,
        Type? resultType = null,
        Dictionary<int, NodeId>? sourceMap = null,
        AnalysisResult? analysisResult = null,
        List<LoopBodyEntry>? loopBodies = null,
        List<string>? callSiteTargets = null) {
        return new Bytecode(Build(), sourceMap ?? [], functions, constants, callSites, exceptionRegions, resultType, analysisResult, loopBodies, callSiteTargets);
    }
}