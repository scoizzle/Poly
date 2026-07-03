using Poly.Syntax.Nodes;

namespace Poly.Syntax.Primitives;

/// <summary>Unconditional jump to a label.</summary>
/// <param name="Target">The label to jump to.</param>
public sealed record Goto(Label Target) : PrimitiveNode {
    public override (int Pop, int Push) StackEffect => (0, 0);
}

/// <summary>Conditional jump — pops the condition and jumps to Target if true; otherwise falls through.</summary>
/// <param name="Target">The label to jump to when the condition is true.</param>
public sealed record CondGoto(Label Target) : PrimitiveNode {
    public override (int Pop, int Push) StackEffect => (1, 0);
}

/// <summary>Pop and return from the current function.</summary>
public sealed record Return : PrimitiveNode {
    public override (int Pop, int Push) StackEffect => (1, 0);
}

/// <summary>Push a literal constant value onto the stack.</summary>
/// <param name="Value">The constant value. Must be a primitive type (int, long, double, bool, string, or null).</param>
public sealed record PushConstant(object? Value) : PrimitiveNode {
    public override (int Pop, int Push) StackEffect => (0, 1);
}

/// <summary>Push the value of a local variable onto the stack.</summary>
/// <param name="SlotIndex">The local variable slot index.</param>
public sealed record LoadLocal(int SlotIndex) : PrimitiveNode {
    public override (int Pop, int Push) StackEffect => (0, 1);
}

/// <summary>Pop a value from the stack and store it into a local variable slot. Pushes the value back (expression semantics).</summary>
/// <param name="SlotIndex">The local variable slot index.</param>
public sealed record StoreLocal(int SlotIndex) : PrimitiveNode {
    public override (int Pop, int Push) StackEffect => (1, 1);
}

/// <summary>Pop two values, apply a binary operation, push the result.</summary>
/// <param name="Op">The binary operation kind.</param>
/// <param name="ComparisonType">When non-null for Eq/Neq ops, indicates both operands are heap handles
/// that should be dereferenced and compared as the given CLR type (reference-type value equality).</param>
public sealed record BinaryOp(OpKind Op, System.Type? ComparisonType = null) : PrimitiveNode {
    public override (int Pop, int Push) StackEffect => (2, 1);
}

/// <summary>Pop one value, apply a unary operation, push the result.</summary>
/// <param name="Op">The unary operation kind.</param>
public sealed record UnaryOp(UnaryOpKind Op) : PrimitiveNode {
    public override (int Pop, int Push) StackEffect => (1, 1);
}

/// <summary>Load a parameter by slot index.</summary>
/// <param name="SlotIndex">The parameter slot index.</param>
public sealed record Parameter(int SlotIndex) : PrimitiveNode {
    public override (int Pop, int Push) StackEffect => (0, 1);
}

/// <summary>Pop and discard the top value.</summary>
public sealed record Discard : PrimitiveNode {
    public override (int Pop, int Push) StackEffect => (1, 0);
}

/// <summary>Duplicate the top value on the stack.</summary>
public sealed record Dup : PrimitiveNode {
    public override (int Pop, int Push) StackEffect => (1, 2);
}

/// <summary>Throw an exception (pops the exception value).</summary>
public sealed record Throw : PrimitiveNode {
    public override (int Pop, int Push) StackEffect => (1, 0);
}

/// <summary>Count set bits (popcount) in a value.</summary>
/// <param name="Operand">The value to count bits in.</param>
public sealed record CountBits : PrimitiveNode {
    public override (int Pop, int Push) StackEffect => (1, 1);
}

/// <summary>Load an element from a heap array.</summary>
/// <param name="ArrayIndex">The array heap handle index.</param>
/// <param name="ElementIndex">The element index within the array.</param>
public sealed record ArrayLoad : PrimitiveNode {
    public override (int Pop, int Push) StackEffect => (2, 1);
}

/// <summary>Allocate a new array on the heap.</summary>
public sealed record NewArray : PrimitiveNode {
    public override (int Pop, int Push) StackEffect => (1, 1);
}

/// <summary>Store a value into an array element. Pops: value, handle, index.</summary>
public sealed record ArrayStore : PrimitiveNode {
    public override (int Pop, int Push) StackEffect => (3, 0);
}

/// <summary>Strided bit-set operation. Pops: handle, start, step, limit.</summary>
public sealed record StridedSet : PrimitiveNode {
    public override (int Pop, int Push) StackEffect => (4, 0);
}

/// <summary>Call a function or method with the specified number of arguments.
/// The target and arguments are already on the stack.</summary>
/// <param name="ArgCount">Number of arguments (excluding the target/callee).</param>
/// <param name="FuncIndex">Index into the program's Functions table for the callee.</param>
public sealed record Call(int ArgCount, int FuncIndex = 0) : PrimitiveNode {
    public override (int Pop, int Push) StackEffect => (ArgCount + 1, 1);
}


/// <summary>Call an external CLR method directly (resolved at compile time).</summary>
/// <param name="Target">The MethodInfo to invoke.</param>
/// <param name="ArgCount">Total argument count (including instance for instance methods).</param>
/// <param name="IsStatic">True if the method is static.</param>
public sealed record CallExternal(System.Reflection.MethodInfo Target, int ArgCount, bool IsStatic) : PrimitiveNode {
    public override (int Pop, int Push) StackEffect => (ArgCount, 1);
}

/// <summary>Load a heap-allocated constant by handle.</summary>
/// <param name="Handle">The heap constant handle.</param>
public sealed record LoadHeapConstant(int Handle) : PrimitiveNode {
    public override (int Pop, int Push) StackEffect => (0, 1);
}

/// <summary>Push a new closure onto the stack.</summary>
/// <param name="LambdaIndex">Index identifying the lambda within the module.</param>
/// <param name="UpvalueCount">Number of captured upvalues to initialize.</param>
public sealed record AllocClosure(int LambdaIndex, int UpvalueCount) : PrimitiveNode {
    public override (int Pop, int Push) StackEffect => (UpvalueCount, 1);
}

/// <summary>Load a captured upvalue from the current closure.</summary>
/// <param name="UpvalueIndex">Index into the closure's upvalue array.</param>
public sealed record LoadUpvalue(int UpvalueIndex) : PrimitiveNode {
    public override (int Pop, int Push) StackEffect => (0, 1);
}

/// <summary>Store a value into a captured upvalue. Pushes the value back (expression semantics).</summary>
/// <param name="UpvalueIndex">Index into the closure's upvalue array.</param>
public sealed record StoreUpvalue(int UpvalueIndex) : PrimitiveNode {
    public override (int Pop, int Push) StackEffect => (1, 1);
}