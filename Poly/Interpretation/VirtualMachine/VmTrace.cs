using System.Runtime.CompilerServices;

namespace Poly.Interpretation.VirtualMachine;

/// <summary>Runtime-gated trace helpers for VM debugging.  Set <see cref="Enabled"/>
/// to true to start logging — no recompilation needed.</summary>
internal static class VmTrace {
    private static bool _enabled;
    private static readonly string FilePath = Environment.GetEnvironmentVariable("VM_TRACE_FILE") ?? "vm_trace.txt";

    [Conditional("VM_TRACE")]
    public static void Enable() => _enabled = true;

    [Conditional("VM_TRACE")]
    public static void Disable() => _enabled = false;

    [Conditional("VM_TRACE")]
    public static void Log(string message) {
        if (!_enabled) return;
        try { System.IO.File.AppendAllText(FilePath, $"{message}\n"); } catch { }
    }

    [Conditional("VM_TRACE")]
    public static void LogOp(int codeOff, byte rawOp, int spOff, long fb) {
        if (!_enabled) return;
        try {
            System.IO.File.AppendAllText(FilePath,
                $"PC={codeOff,4} OP=0x{rawOp:X2} SP={spOff,2} FB={fb}\n");
        }
        catch { }
    }

    [Conditional("VM_TRACE")]
    public static void LogCall(int funcIdx, int argSlots, int pc, int newBase) {
        if (!_enabled) return;
        try {
            System.IO.File.AppendAllText(FilePath,
            $"CALL fi={funcIdx} args={argSlots} targetPC={pc} base={newBase}\n");
        }
        catch { }
    }

    [Conditional("VM_TRACE")]
    public static void LogReturn(int fb, int retPC) {
        if (!_enabled) return;
        try {
            System.IO.File.AppendAllText(FilePath,
            $"RETURN fb={fb} -> PC={retPC}\n");
        }
        catch { }
    }
}