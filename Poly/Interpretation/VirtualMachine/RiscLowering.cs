using System.Collections.Generic;
using System.Reflection;

using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Syntax;
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;

namespace Poly.Interpretation.VirtualMachine;

/// <summary>
/// Analysis-driven lowering from AST Node to linear RISC IR.
/// Supports constants, basic arithmetic, and CLR method calls resolved via CallTargets.
/// Replacements from analysis (constant folding, elision) are honored first.
/// Every emitted instruction is mapped back to a NodeId for fidelity (AtNode, breakpoints, insight).
/// </summary>
internal static class RiscLowering {
    /// <summary>
    /// Lowers the analyzed AST to a RISC program, appending resolved call targets to <paramref name="callTargets"/>.
    /// </summary>
    public static RiscProgram Lower(Node root, AnalysisResult analysis, List<object?> callTargets) {
        var instructions = new List<RiscInstruction>();
        var instrToNode = new Dictionary<int, NodeId>();

        Emit(root, instructions, instrToNode, analysis, callTargets);

        return new RiscProgram(instructions, instrToNode);
    }

    private static void Emit(Node node, List<RiscInstruction> instructions, Dictionary<int, NodeId> instrToNode, AnalysisResult? analysis, List<object?>? callTargets) {
        if (node is null) return;

        // Honor analysis replacements (constant folding, elision decisions, etc.) first.
        var replacement = analysis?.GetNodeReplacement(node);
        if (replacement is not null && !ReferenceEquals(replacement, node)) {
            Emit(replacement, instructions, instrToNode, analysis, callTargets);
            return;
        }

        // Direct constant nodes.
        if (node is Constant constant) {
            long value = ToInt64(constant.Value);
            int pc = instructions.Count;
            instructions.Add(new RiscInstruction(RiscOp.LoadConst, Data: value));
            instrToNode[pc] = node.Id;
            return;
        }

        switch (node) {
            case Add add:
                Emit(add.LeftHandValue, instructions, instrToNode, analysis, callTargets);
                Emit(add.RightHandValue, instructions, instrToNode, analysis, callTargets); {
                    int pc = instructions.Count;
                    instructions.Add(new RiscInstruction(RiscOp.Add));
                    instrToNode[pc] = node.Id;
                }
                return;

            case Subtract sub:
                Emit(sub.LeftHandValue, instructions, instrToNode, analysis, callTargets);
                Emit(sub.RightHandValue, instructions, instrToNode, analysis, callTargets); {
                    int pc = instructions.Count;
                    instructions.Add(new RiscInstruction(RiscOp.Sub));
                    instrToNode[pc] = node.Id;
                }
                return;

            case Multiply mul:
                Emit(mul.LeftHandValue, instructions, instrToNode, analysis, callTargets);
                Emit(mul.RightHandValue, instructions, instrToNode, analysis, callTargets); {
                    int pc = instructions.Count;
                    instructions.Add(new RiscInstruction(RiscOp.Mul));
                    instrToNode[pc] = node.Id;
                }
                return;

            case Invoke invoke:
                EmitInvoke(invoke, instructions, instrToNode, analysis, callTargets);
                return;

            default:
                return;
        }
    }

    private static void EmitInvoke(Invoke invoke, List<RiscInstruction> instructions, Dictionary<int, NodeId> instrToNode, AnalysisResult? analysis, List<object?>? callTargets) {
        // Only emit CALL_EXTERNAL for statically resolved CLR methods.
        if (analysis?.GetResolvedMember(invoke) is not ClrMethod clrMethod)
            return;

        var methodInfo = clrMethod.MethodInfo;
        bool isStatic = clrMethod.LifetimeModifier == Poly.Introspection.LifetimeModifier.Static;

        // Register the call target (append-only — index is stable across re-lowerings).
        int siteIndex = callTargets?.Count ?? 0;
        callTargets?.Add(methodInfo);

        // Emit instance argument first for instance methods.
        if (!isStatic && invoke.Delegate is Member memberAccess) {
            Emit(memberAccess.Value, instructions, instrToNode, analysis, callTargets);
        }

        // Emit method arguments.
        foreach (var arg in invoke.Arguments) {
            Emit(arg, instructions, instrToNode, analysis, callTargets);
        }

        int argCount = invoke.Arguments.Length + (isStatic ? 0 : 1);
        long argBytes = argCount * 8;
        bool hasReturn = methodInfo.ReturnType != typeof(void);

        int pc = instructions.Count;
        instructions.Add(new RiscInstruction(RiscOp.CallExternal,
            Dest: hasReturn ? 1L : 0L,
            Source: argBytes,
            Data: siteIndex));
        instrToNode[pc] = invoke.Id;
    }

    private static long ToInt64(object? value) {
        if (value is null) return 0;
        if (value is long l) return l;
        if (value is int i) return i;
        if (value is double d) return (long)d;
        if (value is bool b) return b ? 1 : 0;
        if (long.TryParse(value.ToString(), out var parsed)) return parsed;
        return 0;
    }
}