namespace Poly.Interpretation.VirtualMachine;

internal sealed class BytecodeBuilder {
    private readonly List<byte> _code = [];
    private readonly Dictionary<string, int> _labels = [];
    private readonly List<(int offset, string label)> _pending = [];
    private int _labelCounter;

    public string NextLabel() => $"L{_labelCounter++}";

    public int Offset => _code.Count;

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

            int targetChunk = targetOffset / 9;
            byte[] operand = BitConverter.GetBytes((long)targetChunk);
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
        Type? resultType = null) {
        return new Bytecode(Build(), [], functions, constants, callSites, exceptionRegions, resultType);
    }
}