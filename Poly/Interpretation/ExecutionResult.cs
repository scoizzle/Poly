using Poly.Interpretation.Vm;

namespace Poly.Interpretation;

/// <summary>
/// Result of a VM execution, carrying both the <see cref="InterpreterResult"/>
/// and the <see cref="VmState"/> for resumption or inspection.
///
/// Typical usage:
/// <code>
/// using var result = Vm.Execute(program);
/// Console.WriteLine(result.GetValue&lt;int&gt;());
/// </code>
///
/// For resumption after suspension:
/// <code>
/// using var result = Vm.Execute(program);
/// if (result.IsSuspended)
///     using var resumed = result.Resume();
/// </code>
/// </summary>
public sealed class ExecutionResult : IDisposable {
    private readonly VmState _state;
    private bool _disposed;

    /// <summary>The <see cref="InterpreterResult"/> from execution.</summary>
    public InterpreterResult Result { get; }

    /// <summary>True when execution was suspended (breakpoint / await).</summary>
    public bool IsSuspended => Result.Kind is InterpreterResult.ResultKind.Suspend;

    /// <summary>True when the result is a value (not void/suspend/signal).</summary>
    public bool HasValue => Result.HasValue;

    /// <summary>Convenience: extract the result as <typeparamref name="T"/>.</summary>
    public T? GetValue<T>() => Result.GetValue<T>();

    /// <summary>Direct access to the underlying state for advanced scenarios
    /// (e.g. inspecting the heap or stack after execution).</summary>
    public VmState State => _state;

    internal ExecutionResult(VmState state, InterpreterResult result) {
        _state = state;
        Result = result;
    }

    /// <summary>
    /// Resume execution after a suspension, using the same <see cref="VmState"/>.
    /// The state is transferred to the new result; this instance becomes a no-op
    /// and should not be used further (the state is no longer valid).
    /// </summary>
    public ExecutionResult Resume(params IEnumerable<object?> args) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsSuspended)
            throw new InvalidOperationException("Cannot resume: execution was not suspended.");

        var state = _state;
        // Transfer ownership — the new result owns the state
        _disposed = true;

        var result = Poly.Interpretation.Vm.Vm.Execute(state, args);
        return new ExecutionResult(state, result);
    }

    /// <summary>The raw top-of-stack value from the VM, or 0 if void.</summary>
    public long RawValue {
        get {
            int sp = _state.Stack.StackPointer;
            return sp > 0 ? _state.Stack.RawSlots[sp - 1] : 0L;
        }
    }

    public void Dispose() {
        if (!_disposed) {
            _disposed = true;
            _state.Dispose();
        }
    }
}