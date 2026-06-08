using System.Collections.Generic;

using Poly.Syntax.Analysis;

namespace Poly.Interpretation.VirtualMachine;

internal sealed record FunctionEntry(int PC, int ArgBytes, int RetBytes, int LocalCount = 0);

internal sealed record ExceptionRegion(int TryStart, int TryEnd, int CatchStart, int? FinallyStart);

internal sealed class Bytecode(
    byte[] code,
    Dictionary<int, NodeId> sourceMap,
    List<FunctionEntry>? functions = null,
    List<object?>? constants = null,
    List<CallSiteDelegate>? callSites = null,
    List<ExceptionRegion>? exceptionRegions = null,
    Type? resultType = null) {
    public byte[] Code { get; } = code;
    public IReadOnlyDictionary<int, NodeId> SourceMap { get; } = sourceMap;
    public IReadOnlyList<FunctionEntry> Functions { get; } = functions ?? [];
    public IReadOnlyList<object?> Constants { get; } = constants ?? [];
    public IReadOnlyList<CallSiteDelegate> CallSites { get; } = callSites ?? [];
    public IReadOnlyList<ExceptionRegion> ExceptionRegions { get; } = exceptionRegions ?? [];
    public Type? ResultType { get; } = resultType;

    public int CodeLength => Code.Length;

    public NodeId? GetNodeIdForInstruction(int pc)
        => SourceMap.TryGetValue(pc, out var id) ? id : null;
}