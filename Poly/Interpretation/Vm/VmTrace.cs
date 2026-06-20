namespace Poly.Interpretation.Vm;

internal static class VmTrace {
    public static void LogUop(int programCounter, string text, int depth, int frameBase, VmState state) {
        state.Trace?.WriteLine($"{programCounter,4} {text,-32} depth={depth,-2} fb={frameBase}");
    }
}