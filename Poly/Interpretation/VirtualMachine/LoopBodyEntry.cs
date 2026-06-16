namespace Poly.Interpretation.VirtualMachine;

public sealed record LoopBodyEntry(int BodyPC, int BodyLength, int ContPC, int ContinuePC, int EndPC, Node BodyNode) {
    public IReadOnlyDictionary<string, int>? ParamIndexMap;
    public IReadOnlyDictionary<string, int>? LocalIndexMap;
}