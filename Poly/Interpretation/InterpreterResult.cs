namespace Poly.Interpretation;

/// <summary>Represents the result of a single VM execution or resumption.
/// Carries the outcome kind (void, value, signal, or suspend) and an
/// optional value or label for control-flow signals (break/continue).</summary>
/// <remarks>This is a read-only record struct. Create instances via the
/// static factory methods (<see cref="Void"/>, <see cref="FromValue"/>,
/// <see cref="Return"/>, <see cref="Break"/>, <see cref="Continue"/>,
/// <see cref="Throw"/>, <see cref="Suspend"/>).</remarks>
public readonly record struct InterpreterResult {
    /// <summary>Classifies the outcome of a VM execution step.</summary>
    public enum ResultKind {
        /// <summary>No value produced (statement execution or empty program).</summary>
        Void,
        /// <summary>A return signal with optional value.</summary>
        Return,
        /// <summary>Break from an enclosing loop (optional label for labeled breaks).</summary>
        Break,
        /// <summary>Continue to the next iteration of an enclosing loop (optional label).</summary>
        Continue,
        /// <summary>An exception was thrown during execution.</summary>
        Throw,
        /// <summary>A value was successfully produced.</summary>
        Value,
        /// <summary>Execution suspended (breakpoint, await, or yield).</summary>
        Suspend,
    }

    /// <summary>The kind of result produced.</summary>
    public ResultKind Kind { get; }

    /// <summary>The value produced (only meaningful when <see cref="Kind"/> is
    /// <see cref="ResultKind.Value"/> or <see cref="ResultKind.Return"/>;
    /// holds the exception for <see cref="ResultKind.Throw"/>).</summary>
    public object? Value { get; }

    /// <summary>Optional label carried by <see cref="ResultKind.Break"/> or
    /// <see cref="ResultKind.Continue"/> signals (null for unlabeled jumps).
    /// Labels are compared by ordinal identity.</summary>
    public string? Label { get; }

    /// <summary>True when the result is <see cref="ResultKind.Void"/>.</summary>
    public bool IsVoid => Kind == ResultKind.Void;

    /// <summary>True when the result carries a produced value
    /// (<see cref="ResultKind.Value"/>).</summary>
    public bool HasValue => Kind == ResultKind.Value;

    /// <summary>True when the result represents a control-flow signal
    /// (return, break, continue, throw, or suspend) rather than a value
    /// or void completion.</summary>
    public bool IsSignal => Kind is ResultKind.Return or ResultKind.Break or ResultKind.Continue or ResultKind.Throw or ResultKind.Suspend;

    private InterpreterResult(ResultKind kind, object? value, string? label = null) {
        Kind = kind;
        Value = value;
        Label = label;
    }

    /// <summary>Extracts the result as <typeparamref name="T"/>, converting
    /// from the VM's uniform long representation as needed.</summary>
    /// <typeparam name="T">The target CLR type for the result value.</typeparam>
    /// <returns>The value converted to <typeparamref name="T"/>, or
    /// <c>default(T?)</c> if <see cref="HasValue"/> is false.
    /// Handles conversions from <c>long</c> to <c>bool</c>, <c>int</c>,
    /// <c>short</c>, and <c>byte</c> in addition to direct casts.</returns>
    public T? GetValue<T>() {
        if (!HasValue) return default!;
        if (Value is null) return default!;
        if (Value is T t) return t;
        if (Value is long l) {
            if (typeof(T) == typeof(bool)) return (T)(object)(l != 0L);
            if (typeof(T) == typeof(int)) return (T)(object)(int)l;
            if (typeof(T) == typeof(short)) return (T)(object)(short)l;
            if (typeof(T) == typeof(byte)) return (T)(object)(byte)l;
            if (typeof(T) == typeof(uint)) return (T)(object)unchecked((uint)l);
            if (typeof(T) == typeof(ulong)) return (T)(object)unchecked((ulong)l);
            if (typeof(T) == typeof(ushort)) return (T)(object)unchecked((ushort)l);
            if (typeof(T) == typeof(double)) return (T)(object)BitConverter.Int64BitsToDouble(l);
            if (typeof(T) == typeof(float)) return (T)(object)(float)BitConverter.Int64BitsToDouble(l);
        }
        if (typeof(T) == typeof(object)) return (T)Value;
        return (T)Convert.ChangeType(Value, typeof(T))!;
    }

    /// <summary>Returns a void result (no value produced).</summary>
    public static InterpreterResult Void => new(ResultKind.Void, null);

    /// <summary>Creates a return signal with an optional value.</summary>
    /// <param name="value">Optional return value (null by default).</param>
    public static InterpreterResult Return(object? value = null) => new(ResultKind.Return, value);

    /// <summary>Creates a break signal for loop exit.</summary>
    /// <param name="label">Optional target label for labeled breaks.</param>
    public static InterpreterResult Break(string? label = null) => new(ResultKind.Break, null, label);

    /// <summary>Creates a continue signal for loop iteration advancement.</summary>
    /// <param name="label">Optional target label for labeled continues.</param>
    public static InterpreterResult Continue(string? label = null) => new(ResultKind.Continue, null, label);

    /// <summary>Creates a throw signal carrying the exception.</summary>
    /// <param name="exception">The exception to propagate.</param>
    public static InterpreterResult Throw(Exception exception) => new(ResultKind.Throw, exception);

    /// <summary>Creates a suspend signal (breakpoint, await, or yield).</summary>
    /// <param name="reason">Optional human-readable reason for the suspension.</param>
    public static InterpreterResult Suspend(string? reason = null) => new(ResultKind.Suspend, reason);

    /// <summary>Creates a value result from any object.</summary>
    /// <param name="value">The produced value (can be null).</param>
    public static InterpreterResult FromValue(object? value) => new(ResultKind.Value, value);
}