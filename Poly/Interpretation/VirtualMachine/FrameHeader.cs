using System.Runtime.InteropServices;

namespace Poly.Interpretation.VirtualMachine;

internal struct FrameHeader {
    public int RetPC;
    public int SavedPrevBase;
    public int ArgSlots;
    public int RetSlots;

    public const int SlotCount = 4;

    public static void Write(Span<int> slots, int baseIdx, int retPC, int savedPrevBase,
        int argSlots, int retSlots) =>
        MemoryMarshal.Write(MemoryMarshal.AsBytes(slots.Slice(baseIdx, SlotCount)),
            new FrameHeader {
                RetPC = retPC,
                SavedPrevBase = savedPrevBase,
                ArgSlots = argSlots,
                RetSlots = retSlots
            });

    public static FrameHeader Read(Span<int> slots, int baseIdx) =>
        MemoryMarshal.Read<FrameHeader>(MemoryMarshal.AsBytes(slots.Slice(baseIdx, SlotCount)));
}