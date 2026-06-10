namespace Poly.Interpretation.VirtualMachine;

/// <summary>
/// Encoding (1 byte):
///   Bit 7: interrupt (set at runtime by debugger for breakpoints)
///   Bit 6: size (0 = 1-byte nullary, 1 = 9-byte operand-bearing)
///   Bits 5-0: opcode (64 values)
/// </summary>
internal enum OpCode : byte {
    // ── Nullary (1 byte in code) ──

    Pop,
    Dup,

    Neg, Not, Add, Sub, Mul, Div, DivRem,

    Eq, Ne, Lt, Le, Gt, Ge,

    BitNot, BitAnd, BitOr, BitXor, Shl, Shr,

    Return,
    LoadValue, StoreValue,
    CallClosure,
    Throw, EndFinally,

    // ── Operand-bearing (9 bytes in code; encoder sets bit 6) ──

    Push,
    Jump,
    JumpIfFalse,
    Call,
    CallExternal,
    AllocClosure,
    LoadArg,
    StoreArg,
    LoadLocal,
    StoreLocal,
    LoadUpvalue,
    StoreUpvalue,
}

internal static class OpCodeEncoding {
    public const byte InterruptBit = 0x80;
    public const byte SizeBit = 0x40;
    public const byte OpcodeMask = 0x3F;

    public static int SizeOf(byte opcodeByte) =>
        (opcodeByte & SizeBit) == 0 ? 1 : 9;
}