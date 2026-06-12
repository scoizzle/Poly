using System.Runtime.CompilerServices;

namespace Poly.Interpretation.VirtualMachine;

/// <summary>Runtime-gated trace helpers for VM debugging.  Set <see cref="Enabled"/>
/// to true to start logging — no recompilation needed.</summary>
internal static class VmTrace {
    private static readonly string FilePath = Environment.GetEnvironmentVariable("VM_TRACE_FILE") ?? "vm_trace.txt";
    private static long _seq;

    [Conditional("VM_TRACE")]
    public static void LogOp(int codeOff, byte rawOp, int spOff, long fb) {
        long s = Interlocked.Increment(ref _seq);
        try {
            File.AppendAllText(FilePath,
            $"{s,6} PC={codeOff,4} OP=0x{rawOp:X2} SP={spOff,2} FB={fb}\n");
        }
        catch { }
    }

    [Conditional("VM_TRACE")]
    public static void LogCall(int funcIdx, int argSlots, int pc, int newBase) {
        long s = Interlocked.Increment(ref _seq);
        try {
            File.AppendAllText(FilePath,
            $"{s,6} CALL fi={funcIdx} args={argSlots} targetPC={pc} base={newBase}\n");
        }
        catch { }
    }

    [Conditional("VM_TRACE")]
    public static void LogReturn(int fb, int retPC) {
        long s = Interlocked.Increment(ref _seq);
        try {
            File.AppendAllText(FilePath,
            $"{s,6} RETURN fb={fb} -> PC={retPC}\n");
        }
        catch { }
    }
}