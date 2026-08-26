namespace Poly.Interpretation.Vm;

/// <summary>
/// Describes a single user variable's name and its offset within the frame.
/// </summary>
public sealed record VariableLayout(string Name, int FrameOffset);

/// <summary>
/// Debug information collected during lowering. Stored in <see cref="VmProgram.DebugInfo"/>.
/// </summary>
public sealed record VmDebugInfo(
    IReadOnlyList<VariableLayout> Variables
);

/// <summary>
/// Result of a single step or continue operation.
/// </summary>
public sealed record DebugResult(
    Node Node,
    IReadOnlyList<(string Name, long Value)> Locals,
    bool IsCompleted = false,
    bool IsSuspend = false,
    Exception? Fault = null
);

/// <summary>
/// Stateful debugger for Poly VM programs. Attaches via <see cref="VmState.DebugHook"/>
/// and provides step-over, continue, and inspection for use inside a neurosymbolic
/// loop or MCP service.
///
/// DESIGN — "always loaded, zero overhead when idle":
/// The hook runs the program on a background thread.  During normal execution the
/// hook checks a <c>volatile bool</c> and returns immediately — no blocking, no
/// event wait.  When <see cref="StepOver"/> is called, it sets a flag so the next
/// hook invocation blocks and signals back.  This means the program runs at full
/// speed when nobody is debugging, and the thread only blocks when actively stepping.
///
/// Usage:
/// <code>
/// using var dbg = new VmDebugger(program);
/// dbg.Start();                    // begin execution, pause at first statement
/// var r1 = dbg.StepOver();        // advance one statement
/// dbg.Continue();                 // run to completion (non-blocking hook)
/// 
/// // Later, re-run with stepping:
/// dbg.Continue();                 // reset — run full speed until done
/// </code>
/// </summary>
public sealed class VmDebugger : IDisposable {
    private readonly VmProgram _program;
    private readonly VmState _state;
    private Task? _execution;
    private readonly AutoResetEvent _hookReady = new(false);   // set by hook when blocked for step
    private readonly AutoResetEvent _stepRelease = new(false);  // set by StepOver to release hook
    private readonly ManualResetEvent _completed = new(false);  // set when delegate finishes
    private volatile bool _stepRequested;   // set by StepOver, read by hook
    private volatile bool _disposed;
    private volatile Exception? _executionException;

    /// <summary>Named local variables at the last statement boundary.
    /// Updated after each <see cref="StepOver"/> or <see cref="Continue"/>.
    /// Read-only snapshot of the current frame's locals with their names
    /// as resolved from <see cref="VmProgram.DebugInfo"/>.</summary>
    public IReadOnlyList<(string Name, long Value)> CurrentLocals { get; private set; }
        = Array.Empty<(string, long)>();

    /// <summary>AST node at the last statement boundary.
    /// Updated after each step or continue operation.
    /// Useful for symbolic debugger UIs and MCP-based tooling.</summary>
    public Node? CurrentNode { get; private set; }

    /// <summary>The underlying VM state. Inspect between steps to examine
    /// the heap, stack, or program counter directly.</summary>
    public VmState State => _state;

    /// <summary>True when the compiled program has completed execution.
    /// Check before calling <see cref="StepOver"/> to avoid waiting.</summary>
    public bool IsCompleted => _completed.WaitOne(0);

    /// <summary>Exception thrown by the compiled delegate, or null if it
    /// completed normally or has not finished.</summary>
    public Exception? ExecutionException => _executionException;

    /// <summary>Creates a new VM debugger for the given compiled program.
    /// Optionally accepts a pre-existing <see cref="VmState"/> for stateful
    /// debugging sessions (e.g. after a suspend).</summary>
    /// <param name="program">The compiled program to debug.</param>
    /// <param name="preexistingState">Optional pre-existing VM state.
    /// If null, a fresh <see cref="VmState"/> is created.</param>
    public VmDebugger(VmProgram program, VmState? preexistingState = null) {
        _program = program;
        _state = preexistingState ?? new VmState(program);
    }

    /// <summary>
    /// Start execution on a background thread.  The hook runs pass-through
    /// (non-blocking) until <see cref="StepOver"/> is called.
    /// Blocks until the first statement boundary is reached.
    /// </summary>
    public DebugResult Start(CancellationToken ct = default) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_execution is not null)
            throw new InvalidOperationException("VM debugger has already been started.");
        _state.Status = InterpreterStatus.Running;
        _state.Registers ??= new long[256];
        _state.DebugHook = DebugHookHandler;

        // Set step flag before launching so the first hook blocks immediately.
        // This ensures Start() returns paused at the first statement.
        _stepRequested = true;

        _execution = Task.Run(() => {
            try { _program.Delegate(_state); }
            catch (Exception ex) { _executionException = ex; }
            finally { _completed.Set(); _hookReady.Set(); }
        });

        WaitForBoundary(ct);
        if (_completed.WaitOne(0))
            return CaptureCompleted();
        return CaptureResult();
    }

    /// <summary>
    /// Advance one statement boundary.  Sets the step flag so the next hook
    /// invocation blocks, then waits for it.  Returns the debug result at
    /// the new statement boundary.
    /// </summary>
    public DebugResult StepOver(CancellationToken ct = default) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureStarted();

        _stepRequested = true;

        // Release the hook if it was blocked from the previous step
        _stepRelease.Set();

        WaitForBoundary(ct);

        if (_completed.WaitOne(0))
            return CaptureCompleted();
        return CaptureResult();
    }

    /// <summary>
    /// Run to completion or until a SuspendNode.  The hook runs pass-through
    /// (non-blocking) — no per-statement synchronization overhead.
    /// </summary>
    public DebugResult Continue(CancellationToken ct = default) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureStarted();

        // Clear step flag — hook runs free from now on
        _stepRequested = false;

        // Release any currently-blocked hook
        _stepRelease.Set();

        var handles = ct.CanBeCanceled
            ? new WaitHandle[] { _completed, _hookReady, ct.WaitHandle }
            : new WaitHandle[] { _completed, _hookReady };
        while (true) {
            WaitHandle.WaitAny(handles);
            ct.ThrowIfCancellationRequested();
            if (_completed.WaitOne(0))
                return CaptureCompleted();
            if (_state.Status == InterpreterStatus.Suspended)
                return CaptureResult();
        }
    }

    private void EnsureStarted() {
        if (_execution is null)
            throw new InvalidOperationException("VM debugger has not been started.");
    }

    private void WaitForBoundary(CancellationToken ct) {
        var handles = ct.CanBeCanceled
            ? new WaitHandle[] { _hookReady, _completed, ct.WaitHandle }
            : new WaitHandle[] { _hookReady, _completed };
        WaitHandle.WaitAny(handles);
        if (ct.IsCancellationRequested) {
            _stepRelease.Set();
            ct.ThrowIfCancellationRequested();
        }
    }

    // ── Hook ─────────────────────────────────────────────────

    private void DebugHookHandler(Node node, ReadOnlySpan<long> localsSpan, Heap heap) {
        if (_disposed) return;

        // Pass-through: if nobody's stepping, return immediately.
        if (!_stepRequested) return;

        CurrentLocals = GetLocals(_program, localsSpan);
        CurrentNode = node;

        // Someone wants to step — signal that we're at a boundary and block
        // until they tell us to continue.
        //
        // Do NOT clear _stepRequested here. StepOver leaves it true so the
        // next statement boundary also pauses; Continue() clears it for
        // full-speed run-to-completion. Clearing it in the hook made multi-
        // step impossible (every StepOver would free-run to the end).
        _hookReady.Set();
        _stepRelease.WaitOne();
    }

    // ── Result capture ───────────────────────────────────────

    private DebugResult CaptureResult() {
        // Use the snapshot already captured in the hook (CurrentLocals) rather than
        // re-reading from state.Stack.RawSlots, which may contain dirty ArrayPool
        // data if the emitter hasn't flushed variables yet.
        var r = new DebugResult(
            Node: _state.CurrentAstNode ?? new Constant(0L),
            Locals: CurrentLocals,
            IsSuspend: _state.Status == InterpreterStatus.Suspended);
        CurrentNode = r.Node;
        return r;
    }

    private DebugResult CaptureCompleted() {
        // Use CurrentLocals (set by the last hook invocation) — at completion
        // the hook has already captured the final state.
        var r = new DebugResult(
            Node: _state.CurrentAstNode ?? new Constant(0L),
            Locals: CurrentLocals,
            IsCompleted: true,
            Fault: _executionException);
        CurrentNode = r.Node;
        return r;
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _state.DebugHook = null;
        _stepRelease.Set();  // release any blocked hook
        _hookReady.Dispose();
        _stepRelease.Dispose();
        _completed.Dispose();
    }

    // ── Static helpers (also usable standalone) ────────────────

    /// <summary>Named local resolution from compile-time debug info.</summary>
    public static IReadOnlyList<(string Name, long Value)> GetLocals(
        VmProgram program, ReadOnlySpan<long> localsSpan) {
        var debugInfo = program.DebugInfo as VmDebugInfo;
        if (debugInfo is null || debugInfo.Variables.Count == 0)
            return Array.Empty<(string, long)>();
        var result = new (string, long)[debugInfo.Variables.Count];
        for (int i = 0; i < debugInfo.Variables.Count; i++) {
            var v = debugInfo.Variables[i];
            result[i] = (v.Name, (uint)v.FrameOffset < (uint)localsSpan.Length
                ? localsSpan[v.FrameOffset]
                : 0L);
        }
        return result;
    }

    /// <summary>Named local resolution from post-execution state.</summary>
    public static IReadOnlyList<(string Name, long Value)> GetLocals(VmState state) {
        var debugInfo = state.Program.DebugInfo as VmDebugInfo;
        if (debugInfo is null || debugInfo.Variables.Count == 0)
            return Array.Empty<(string, long)>();
        var slots = state.Stack.RawSlots;
        const int fp = 0;
        var result = new (string, long)[debugInfo.Variables.Count];
        for (int i = 0; i < debugInfo.Variables.Count; i++) {
            var v = debugInfo.Variables[i];
            result[i] = (v.Name, (fp + v.FrameOffset < slots.Length)
                ? slots[fp + v.FrameOffset] : 0L);
        }
        return result;
    }

    /// <summary>Human-readable frame summary.</summary>
    public static string FormatCurrentFrame(VmState state) {
        var node = state.CurrentAstNode;
        var nodeName = node?.GetType().Name ?? "?";
        var locals = GetLocals(state);
        return locals.Count == 0
            ? $"{nodeName} (no locals)"
            : $"{nodeName} {{{string.Join(", ", locals.Select(l => $"{l.Name}={l.Value}"))}}}";
    }
}