using Poly.Syntax.Analysis;

namespace Poly.Interpretation.VirtualMachine;

internal enum RiscType : long {
    i64,
    i32,
    i16,
    i8,
    u64,
    u32,
    u16,
    u8,
    f64,
    f32,
    Handle // Signed for stack-relative, unsigned for heap-relative (or external)
}

internal enum RiscOp : byte {
    Nop = 0,

    // Data movement and constants
    LoadConst = 1,
    LoadConstHandle = 2,

    // Sized bulk memory (unified stack/heap via sign of handle)
    // Before these, the caller typically pushes: size (i64), then signed handle; or use immediates in payload.
    LoadValue = 10,
    StoreValue = 11,

    // Stack management
    Dup = 20,
    Pop = 21,

    // === Wide machine arithmetic and comparisons ===
    // The VM core *only* implements operations on the "native" wide scalar types:
    //   - i64 / u64  (signed and unsigned 64-bit integers)
    //   - double     (IEEE 754 binary64)
    //
    // All source-level smaller numeric types (i32, u32, i16, f32, f16, etc.) are represented
    // by first performing the operation in the corresponding wide type, then emitting an
    // explicit Narrow* instruction (see below) to enforce the down-scaling / bit pattern / range.
    //
    // This keeps the dispatch table and implementation tiny while still allowing lowering
    // (driven by AnalysisResult type information) to insert the correct narrowing at the right points.

    Add = 30,
    Sub = 31,
    Mul = 32,
    Div = 33,
    Mod = 34,
    Neg = 35,

    // Wide comparisons (result 0/1 as i64 on stack)
    Eq = 40,
    Ne = 41,
    Lt = 42,
    Le = 43,
    Gt = 44,
    Ge = 45,

    // === Explicit narrowing / down-scaling ===
    // These take a wide value (i64/u64 bits or double bits) from the stack, convert it
    // to the target smaller representation (with language-defined truncation, wrap, rounding,
    // NaN handling etc.), and push the result back (still occupying an 8-byte slot, but with
    // the correct low bits / sign/zero extension as appropriate).
    //
    // Lowering is responsible for inserting the right Narrow* based on the static type of
    // the expression and the original AST node kinds.
    Narrow = 50,

    // Control flow
    Jump = 80,
    JumpIfFalse = 81,

    // Calls and frames (frames are stack segments)
    Call = 82,           // internal: caller must push argByteCount then targetPC before this op
    CallExternal = 83,   // caller pushes argData..., argByteCount, siteOrHandle, hasRetFlag before this op
    Return = 84,         // caller must push retByteSize then argByteSize before this op

    // Suspension point (for neurosymbolic suspend/resume + insight)
    Suspend = 90,
}

internal readonly record struct RiscInstruction(RiscOp Op, long Dest = 0, long Source = 0, long Data = 0) {
    public RiscType Type => Op switch {
        RiscOp.LoadConst or RiscOp.LoadConstHandle => RiscType.Handle,
        RiscOp.Add or RiscOp.Sub or RiscOp.Mul or RiscOp.Div or RiscOp.Mod or RiscOp.Neg => (RiscType)Source,
        RiscOp.Eq or RiscOp.Ne or RiscOp.Lt or RiscOp.Le or RiscOp.Gt or RiscOp.Ge => RiscType.i64,
        RiscOp.Narrow => (RiscType)Source,
        _ => 0, // Nop, Dup, Pop, Jump*, Call*, Return, Suspend have no specific type
    };
    public override string ToString() => $"{Op} Dest={Dest} Source={Source} Data={Data}";
}