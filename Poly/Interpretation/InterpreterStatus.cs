namespace Poly.Interpretation;

/// <summary>Describes the lifecycle state of a VM execution session.</summary>
public enum InterpreterStatus {
    /// <summary>The compiled delegate is actively executing (or about to execute).
    /// Set before <c>state.Program.Delegate(state)</c> is invoked and updated
    /// by the delegate as control flows through the program.</summary>
    Running,

    /// <summary>Execution has been suspended (e.g. at a breakpoint or await
    /// boundary). The <see cref="VmState"/> retains all stack and heap state
    /// and can be resumed via <see cref="Interpreter.Resume"/>.</summary>
    Suspended,

    /// <summary>Execution has completed normally. The result value (if any)
    /// is on the top of the value stack.</summary>
    Completed,

    /// <summary>Execution is being resumed after a suspension. The preamble
    /// re-reads <see cref="VmState.FramePos"/> and dispatches to the correct
    /// program counter to continue execution from the pause point.</summary>
    Resuming,
}