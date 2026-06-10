using System.Reflection;

using Poly.Syntax.Analysis;

namespace Poly.Interpretation.VirtualMachine;

internal sealed record FunctionEntry(int PC, int ArgBytes, int RetBytes, int LocalCount = 0) {
    public int HotCount;
    public CallSiteDelegate? NativeFn;
    public Node? SourceNode;
}
internal sealed record ExceptionRegion(int TryStart, int TryEnd, int CatchStart, int? FinallyStart);

internal sealed class Bytecode {
    public byte[] Code { get; }
    public IReadOnlyDictionary<int, NodeId> SourceMap { get; }
    public IReadOnlyList<FunctionEntry> Functions { get; }
    public IReadOnlyList<object?> Constants { get; }
    public IReadOnlyList<CallSiteDelegate> CallSites { get; }
    public IReadOnlyList<ExceptionRegion> ExceptionRegions { get; }
    public Type? ResultType { get; }
    public AnalysisResult? AnalysisResult { get; }
    public int CodeLength => Code.Length;

    public Bytecode(
        byte[] code,
        Dictionary<int, NodeId> sourceMap,
        List<FunctionEntry>? functions = null,
        List<object?>? constants = null,
        List<CallSiteDelegate>? callSites = null,
        List<ExceptionRegion>? exceptionRegions = null,
        Type? resultType = null,
        AnalysisResult? analysisResult = null) {
        Code = code;
        SourceMap = sourceMap;
        Functions = functions ?? [];
        Constants = constants ?? [];
        CallSites = callSites ?? [];
        ExceptionRegions = exceptionRegions ?? [];
        ResultType = resultType;
        AnalysisResult = analysisResult;
    }

    public NodeId? GetNodeIdForInstruction(int pc) =>
        SourceMap.TryGetValue(pc, out var id) ? id : null;
}