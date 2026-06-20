namespace Poly.Interpretation.Vm;

internal sealed class Closure(int funcIndex, int captureCount) {
    public int FuncIndex { get; } = funcIndex;
    public object?[] Captures { get; } = new object?[captureCount];
}
