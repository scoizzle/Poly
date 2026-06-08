namespace Poly.Interpretation.VirtualMachine;

internal sealed class Closure {
    public int FuncIndex { get; }
    public object?[] Captures { get; }

    public Closure(int funcIndex, int captureCount) {
        FuncIndex = funcIndex;
        Captures = new object?[captureCount];
    }
}