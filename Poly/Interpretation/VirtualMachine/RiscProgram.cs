using System.Collections.Generic;

using Poly.Syntax.Analysis;

namespace Poly.Interpretation.VirtualMachine;

internal sealed class RiscProgram {
    public IReadOnlyList<RiscInstruction> Instructions { get; }
    public IReadOnlyDictionary<int, NodeId> InstrToNode { get; }

    public RiscProgram(List<RiscInstruction> instructions, Dictionary<int, NodeId> instrToNode) {
        Instructions = instructions;
        InstrToNode = instrToNode;
    }

    public int InstructionCount => Instructions.Count;

    public RiscInstruction GetInstruction(int pc) => Instructions[pc];

    public NodeId? GetNodeIdForInstruction(int pc)
        => InstrToNode.TryGetValue(pc, out var id) ? id : null;
}