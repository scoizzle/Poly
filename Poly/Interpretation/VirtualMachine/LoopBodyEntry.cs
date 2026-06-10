namespace Poly.Interpretation.VirtualMachine;

internal enum LoopResult { Normal, Break, Continue }

internal delegate LoopResult LoopBodyDelegate(VmState state);

internal sealed record LoopBodyEntry(int BodyPC, int BodyLength, int ContPC, int ContinuePC, int EndPC, Node BodyNode) {
    public int HotCount;
    public LoopBodyDelegate? NativeFn;
    public IReadOnlyDictionary<string, int>? ParamIndexMap;
    public IReadOnlyDictionary<string, int>? LocalIndexMap;
}