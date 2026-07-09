namespace Poly.Interpretation.Vm;

/// <summary>
/// Describes a single user variable's name and its offset within the frame.
/// Used by debuggers to map raw <c>_slots[_fb + offset]</c> values to named locals.
/// </summary>
public sealed record VariableLayout(string Name, int FrameOffset);

/// <summary>
/// Debug information collected during lowering. Stored in <see cref="VmProgram.DebugInfo"/>.
/// </summary>
public sealed record VmDebugInfo(
    /// <summary>All user-visible variables with their frame offsets.</summary>
    IReadOnlyList<VariableLayout> Variables
);

/// <summary>
/// Helper for resolving symbolic debug information from a suspended or running
/// <see cref="VmState"/> and its <see cref="VmProgram"/>.
///
/// Currently supports single-frame local resolution. Full multi-frame walk
/// (via PreviousFramePointer/SavedStackPointer) requires the 2-word frame
/// header runtime emission (tracked in ABI-001).
/// </summary>
public static class VmDebugger {
    /// <summary>
    /// Returns the named locals for the current frame, resolved from
    /// <see cref="VmProgram.DebugInfo"/> and the live frame's locals span.
    ///
    /// Useful inside a DebugHook callback where the locals span is provided
    /// by the emitter (compile-time offset-based snapshot).
    /// </summary>
    public static IReadOnlyList<(string Name, long Value)> GetLocals(
        VmProgram program, ReadOnlySpan<long> localsSpan) {
        var debugInfo = program.DebugInfo as VmDebugInfo;
        if (debugInfo is null || debugInfo.Variables.Count == 0)
            return Array.Empty<(string, long)>();

        var result = new (string, long)[debugInfo.Variables.Count];
        int count = Math.Min(debugInfo.Variables.Count, localsSpan.Length);

        for (int i = 0; i < count; i++) {
            var v = debugInfo.Variables[i];
            result[i] = (v.Name, localsSpan[v.FrameOffset]);
        }

        // If there are more variables than span length, pad with 0
        for (int i = count; i < debugInfo.Variables.Count; i++) {
            result[i] = (debugInfo.Variables[i].Name, 0L);
        }

        return result;
    }

    /// <summary>
    /// Returns the named locals for the current frame, resolved from
    /// <see cref="VmProgram.DebugInfo"/> and <see cref="VmState"/>.
    ///
    /// Reads variable values from <c>state.Stack.RawSlots</c> using the
    /// layout captured at compile time. For root frames (the common case),
    /// slot 0 is the frame base.
    ///
    /// NOTE: After execution completes, the result value may have overwritten
    /// the slot at offset 0 (the first local). For accurate local values
    /// during execution, use <see cref="GetLocals(VmProgram, ReadOnlySpan{long})"/>
    /// from within a DebugHook callback.
    /// </summary>
    public static IReadOnlyList<(string Name, long Value)> GetLocals(VmState state) {
        var debugInfo = state.Program.DebugInfo as VmDebugInfo;
        if (debugInfo is null || debugInfo.Variables.Count == 0)
            return Array.Empty<(string, long)>();

        var slots = state.Stack.RawSlots;
        const int fp = 0; // root frame always starts at slot 0
        var result = new (string, long)[debugInfo.Variables.Count];

        for (int i = 0; i < debugInfo.Variables.Count; i++) {
            var v = debugInfo.Variables[i];
            long value = (fp + v.FrameOffset < slots.Length)
                ? slots[fp + v.FrameOffset]
                : 0L;
            result[i] = (v.Name, value);
        }

        return result;
    }

    /// <summary>
    /// Returns a human-readable stack trace entry for the current state,
    /// showing the AST node type and named local variables.
    /// </summary>
    public static string FormatCurrentFrame(VmState state) {
        var node = state.CurrentAstNode;
        var nodeName = node?.GetType().Name ?? "?";
        var locals = GetLocals(state);

        if (locals.Count == 0)
            return $"{nodeName} (no locals)";

        var vars = string.Join(", ", locals.Select(l => $"{l.Name}={l.Value}"));
        return $"{nodeName} {{{vars}}}";
    }
}