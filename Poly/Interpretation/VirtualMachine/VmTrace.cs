namespace Poly.Interpretation.VirtualMachine;

/// <summary>Runtime-gated µop trace.  Set <c>state.Trace = Console.Out</c>
/// (or any <c>TextWriter</c>) to start logging — no recompilation needed.
/// When <c>state.Trace</c> is null (the default) the cost per µop is a
/// null-conditional check — ~1 ns.</summary>
internal static class VmTrace {
    public static void LogUop(int pc, string text, int sp, int fb, VmState state) {
        state.Trace?.WriteLine($"{pc,4} {text,-32} sp={sp,-2} fb={fb}");
    }
}