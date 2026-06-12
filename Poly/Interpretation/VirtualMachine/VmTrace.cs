using System.Runtime.CompilerServices;

namespace Poly.Interpretation.VirtualMachine;

/// <summary>Runtime-gated trace helpers for VM debugging.  Set
/// <c>state.Trace = Console.Out</c> (or any <c>TextWriter</c>)
/// to start logging — no recompilation needed.  When <c>state.Trace</c>
/// is null (the default) the cost per µop is a null check + predictable
/// branch — ~1 ns.</summary>
internal static class VmTrace {
    private static readonly string FilePath = Environment.GetEnvironmentVariable("VM_TRACE_FILE") ?? "vm_trace.txt";
    private static long _seq;

    /// <summary>Runtime µop trace: writes to <c>state.Trace</c> if set,
    /// otherwise to <c>$VM_TRACE_FILE</c>.  The null check on
    /// <c>state.Trace</c> makes this cheap when tracing is off.</summary>
    public static void LogUop(int pc, string text, VmState state) {
        var w = state.Trace;
        if (w is not null) {
            w.WriteLine($"{pc,4}: {text}");
            return;
        }
        string? f = FilePath;
        if (f is null || f == "vm_trace.txt" && !File.Exists(f)) return;
        long s = Interlocked.Increment(ref _seq);
        try { File.AppendAllText(f, $"{s,6} UOP {pc,4}: {text}\n"); }
        catch { }
    }

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