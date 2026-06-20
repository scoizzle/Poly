using System.Linq.Expressions;

using Poly.Syntax;

namespace Poly.Interpretation.Vm.Instructions;

/// <summary>
/// Base record for all VM instructions. Each instruction compiles itself into a
/// LINQ expression via <see cref="ToExpression"/>, operating on the
/// <see cref="CompilationContext"/>'s evaluation stack (Push/Pop).
///
/// Inline docs per instruction cover:
/// - Stack effect: which and how many values it pops/pushes
/// - Register spill: whether it spills/fills state.Registers and why
/// - State dependencies: what VmState fields it reads/writes
/// - Calling convention: how args are passed, where return value goes
/// - Suspend behavior: whether this instruction can suspend
/// - Assumptions: what must be true before execution
/// </summary>
public abstract record Instruction(NodeId? SourceNodeId) {
    /// <summary>PCs of the µops that produced the values consumed by this instruction.
    /// Set by ResolveProducers during producer tracking.</summary>
    public int[]? ConsumedFromPcs { get; init; }

    /// <summary>When φ is needed: for each consumed value, the source PC whose
    /// path produced a different value.  In tandem with <see cref="PhiAltPcs"/>:
    /// if state.ProgramCounter matches the source PC, use _v{PhiAltPc} instead
    /// of _v{ConsumedFromPc}.</summary>
    public int[]? PhiSourcePcs { get; init; }

    /// <summary>Alternative producer PCs for φ.  Paired with <see cref="PhiSourcePcs"/>.</summary>
    public int[]? PhiAltPcs { get; init; }

    /// <summary>Number of values this instruction pops from the eval stack.
    /// Used by the producer resolver to compute ConsumedFromPcs.</summary>
    public abstract int PopCount { get; }

    /// <summary>Number of values this instruction pushes to the eval stack.
    /// Used by the producer resolver to track producer PCs.</summary>
    public abstract int PushCount { get; }

    public abstract Expression? ToExpression(CompilationContext ctx);
}

public enum BinOpKind {
    Add, Sub, Mul, Div, Mod,
    And, Or, Xor, Shl, Shr,
    Eq, Ne, Lt, Le, Gt, Ge
}

public enum UnaryOpKind {
    Neg, Not, BitNot
}