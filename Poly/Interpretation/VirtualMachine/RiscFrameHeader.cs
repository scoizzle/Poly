namespace Poly.Interpretation.VirtualMachine;

/// <summary>
/// Layout of the on-stack frame header written by CALL and read by RETURN.
/// All values are 8 bytes (i64). Total header Size bytes.
/// The header lives at the frame base; the caller's perspective (base at issuance) is stored
/// so that stack references (negated absolutes) issued in this frame can be created correctly.
/// At deref time for provided handles we use the absolute value directly (self-contained).
/// </summary>
internal static class RiscFrameHeader {
    public const int RetPCOffset = 0;
    public const int SavedPrevBaseOffset = 8;
    public const int CallerPerspectiveOffset = 16; // frame base of issuer (used only at handle creation)
    public const int ArgBytesOffset = 24;
    public const int Size = 32;

    public static void WriteHeader(Span<byte> stackAtBase, int retPC, int savedPrevBase, int callerPerspective, int argBytes) {
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(stackAtBase[..8], retPC);
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(stackAtBase.Slice(SavedPrevBaseOffset, 8), savedPrevBase);
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(stackAtBase.Slice(CallerPerspectiveOffset, 8), callerPerspective);
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(stackAtBase.Slice(ArgBytesOffset, 8), argBytes);
    }

    public static (int retPC, int savedPrevBase, int callerPerspective, int argBytes) ReadHeaderEx(ReadOnlySpan<byte> stackAtBase) {
        var ret = (int)System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(stackAtBase[..8]);
        var prev = (int)System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(stackAtBase.Slice(SavedPrevBaseOffset, 8));
        var persp = (int)System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(stackAtBase.Slice(CallerPerspectiveOffset, 8));
        var args = (int)System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(stackAtBase.Slice(ArgBytesOffset, 8));
        return (ret, prev, persp, args);
    }

    // Compatibility shims so RiscLowering (and any other code) that was written against the older header
    // continues to compile while we focus on core VM instruction handling.
    public static void WriteHeader(Span<byte> stackAtBase, int retPC, int savedPrevBase, int callerPerspective)
        => WriteHeader(stackAtBase, retPC, savedPrevBase, callerPerspective, 0);

    public static (int retPC, int savedPrevBase, int callerPerspective) ReadHeader(ReadOnlySpan<byte> stackAtBase) {
        var (r, p, c, _) = ReadHeaderEx(stackAtBase);
        return (r, p, c);
    }
}