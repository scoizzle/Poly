using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Poly.Interpretation.VirtualMachine;

/// <summary>
/// Byte-backed operand stack for the RISC VM.
/// 8-byte aligned slots. Supports direct Span<byte> reservation for zero-copy writes
/// (no temporary stackallocs for conversions). Separate tag array for development/insight
/// (removable later once IR is strict).
/// Negative handles are negated absolute byte offsets into the stack bytes.
/// </summary>
internal sealed class RiscValueStack : IDisposable {
    private const int SlotSize = 8;
    private const int DefaultInitialBytes = 64 * 1024; // 64 KiB starting point

    private readonly MemoryPool<byte> _pool;
    private IMemoryOwner<byte> _memoryOwner;
    private Memory<byte> _memory;

    // Parallel tag storage (one byte per slot). Length == _sp / SlotSize when in sync.
    private byte[] _tags = Array.Empty<byte>();

    private bool _disposed;

    public RiscValueStack(MemoryPool<byte>? pool = null, int initialCapacityBytes = DefaultInitialBytes) {
        _pool = pool ?? MemoryPool<byte>.Shared;
        var aligned = AlignUp(initialCapacityBytes, SlotSize);
        _memoryOwner = _pool.Rent(aligned);
        _memory = _memoryOwner.Memory[..aligned];
        SP = 0;
        EnsureTagCapacity(0);
    }

    public int SP { get; private set; }
    public int SlotCount => SP / SlotSize;
    public bool IsEmpty => SP == 0;

    public Span<byte> AsSpan() => _memory.Span[..SP];
    public Memory<byte> AsMemory() => _memory[..SP];

    /// <summary>
    /// Returns a writable Span<byte> of the given size for the caller to write into directly, and advances the stack pointer by the padded size.
    /// The caller is responsible for writing the intended logical size (which may be less than the padded size) and for any necessary conversions.
    /// This method ensures that the stack pointer remains aligned to the slot size after the reservation.
    /// </summary>
    public Span<byte> ReserveBytes(int size) {
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        var padded = AlignUp(size, SlotSize);
        // Ensure capacity for the view we will slice (the logical size) plus SP advance.
        int needed = SP + padded;
        EnsureCapacity(needed);

        var dest = _memory.Span.Slice(SP, padded);
        // Advance past padded slot so subsequent ops are aligned.
        SP += padded;

        EnsureTagCapacity(SP / SlotSize);
        // Tag the new slots (0 = unknown for now; higher layers will set).
        var startSlot = (SP - padded) / SlotSize;
        var endSlot = SP / SlotSize;
        for (int s = startSlot; s < endSlot; s++) {
            _tags[s] = 0;
        }

        return dest;
    }

    public void Push<T>(T value) where T : unmanaged {
        var size = Unsafe.SizeOf<T>();
        var dest = ReserveBytes(size);
        if (size < dest.Length)
            dest.Clear();
        MemoryMarshal.Write(dest, in value);
    }

    public T Pop<T>() where T : unmanaged {
        int size = Unsafe.SizeOf<T>();
        if (SP < size)
            throw new InvalidOperationException("Stack underflow");

        SP -= AlignUp(size, SlotSize);
        var source = _memory.Span.Slice(SP, size);
        return MemoryMarshal.Read<T>(source);
    }

    /// <summary>
    /// Pop two values of the given types in a single operation, with a single SP adjustment.
    /// </summary>
    /// <typeparam name="T1">The type of the first value to pop.</typeparam>
    /// <typeparam name="T2">The type of the second value to pop.</typeparam>
    /// <returns>A tuple containing the two popped values.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the stack does not contain enough elements.</exception>
    /// <remarks>
    /// The stack pointer is adjusted once for the total size of both values, ensuring efficient access.
    /// The values are read from the stack in the order they were pushed, with T1 being the most recently pushed value and T2 being the one before it. 
    /// </remarks>
    public (T1 value1, T2 value2) Pop2<T1, T2>() where T1 : unmanaged where T2 : unmanaged {
        var size1 = Unsafe.SizeOf<T1>();
        var segment1Size = AlignUp(size1, SlotSize);
        var size2 = Unsafe.SizeOf<T2>();
        var segment2Size = AlignUp(size2, SlotSize);
        var totalSize = segment1Size + segment2Size;

        if (SP < totalSize)
            throw new InvalidOperationException("Stack underflow");

        SP -= totalSize;
        var source = _memory.Span.Slice(SP, totalSize);

        var value1 = MemoryMarshal.Read<T1>(source);
        var value2 = MemoryMarshal.Read<T2>(source[segment1Size..]);
        return (value1, value2);
    }

    public Span<byte> Peek(int byteOffsetFromTop = 0, int size = SlotSize) {
        var abs = SP - byteOffsetFromTop;
        if (abs < 0 || abs > SP)
            throw new InvalidOperationException("Peek out of range");
        return _memory.Span.Slice(abs, size);
    }

    /// <summary>
    /// Peek a 64-bit value at byte offset from top (negative offset) or absolute.
    /// Per design, support offset-based peeking.
    /// </summary>
    public long Peek64(int byteOffsetFromTop = 0) {
        var abs = SP - 8 - byteOffsetFromTop;
        if (abs < 0 || abs + 8 > SP)
            throw new InvalidOperationException("Peek out of range");
        var source = _memory.Span.Slice(abs, 8);
        return MemoryMarshal.Read<long>(source);
    }

    /// <summary>
    /// Drop (consume without using) the top logicalSize bytes (padded internally to slot).
    /// Used after STORE_VALUE to remove the source value bytes.
    /// </summary>
    public void DropBytes(int logicalSize) {
        if (logicalSize <= 0) return;
        int padded = AlignUp(logicalSize, SlotSize);
        if (SP < padded)
            throw new InvalidOperationException("Stack underflow on drop");
        SP -= padded;
    }

    /// <summary>
    /// Hard truncate the stack pointer. Used by RETURN to remove frame segments while
    /// preserving (after copy) any return value for the caller.
    /// </summary>
    public void TruncateTo(int targetSP) {
        if (targetSP < 0 || targetSP > SP)
            throw new ArgumentOutOfRangeException(nameof(targetSP));
        SP = targetSP;
    }

    /// <summary>
    /// Resolve a signed handle: positive = heap (caller handles via RiscHeap),
    /// negative = negated absolute byte offset into this stack (return the real positive offset).
    /// </summary>
    public static int ResolveStackHandle(long handle) {
        if (handle >= 0)
            throw new ArgumentException("Handle is not a stack reference (positive = heap)", nameof(handle));
        var real = (int)(-handle);
        if (real < 0)
            throw new ArgumentOutOfRangeException(nameof(handle), "Invalid negated stack handle");
        return real;
    }

    public void GrowIfNeeded(int additionalBytes) {
        if (SP + additionalBytes > _memory.Length)
            Grow(additionalBytes);
    }

    private void EnsureCapacity(int requiredBytes) {
        if (requiredBytes > _memory.Length)
            Grow(requiredBytes - _memory.Length);
    }

    private void Grow(int atLeastAdditional) {
        var required = SP + AlignUp(atLeastAdditional, SlotSize);
        var newSize = Math.Max(_memory.Length * 2, required);
        var newOwner = _pool.Rent(newSize);
        var newMem = newOwner.Memory[..newSize];

        _memory.Span[..SP].CopyTo(newMem.Span);

        _memoryOwner.Dispose();
        _memoryOwner = newOwner;
        _memory = newMem;

        // Note: on growth, live negative handle *values* that reside in the stack bytes
        // must be patched (they are absolute, but if we ever stored relative forms or if
        // base adjustment logic appears, we adjust). For pure negated-absolutes the values
        // themselves do not change; only frameBases and any relative issuance do.
        // Tag scan can be used by callers to locate candidate slots when we add richer tagging.
        EnsureTagCapacity(SP / SlotSize);
    }

    private void EnsureTagCapacity(int requiredSlots) {
        if (_tags.Length < requiredSlots) {
            var newLen = Math.Max(requiredSlots, _tags.Length == 0 ? 64 : _tags.Length * 2);
            Array.Resize(ref _tags, newLen);
        }
    }

    private static int AlignUp(int value, int alignment) =>
        (value + alignment - 1) & ~(alignment - 1);

    public void Dispose() {
        if (_disposed) return;
        _memoryOwner.Dispose();
        _disposed = true;
    }
}