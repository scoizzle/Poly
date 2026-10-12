namespace Poly.Interpretation.Vm;

[Obsolete("Unreferenced dead code; no callers. Slated for removal.")]
internal sealed class Closure(int funcIndex, int captureCount) {
    public int FuncIndex { get; } = funcIndex;
    public object?[] Captures { get; } = new object?[captureCount];
}