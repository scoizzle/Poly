namespace Poly.Interpretation;

public readonly record struct InterpreterResult {
    public enum ResultKind { Void, Return, Break, Continue, Throw, Value, Suspend }

    public ResultKind Kind { get; }
    public object? Value { get; }
    public InterpreterSignal? Signal { get; }

    public bool IsVoid => Kind == ResultKind.Void;
    public bool HasValue => Kind == ResultKind.Value;
    public bool IsSignal => Kind is ResultKind.Return or ResultKind.Break or ResultKind.Continue or ResultKind.Throw or ResultKind.Suspend;
    public string? Label => Signal?.Label;

    private InterpreterResult(ResultKind kind, object? value, InterpreterSignal? signal = null) {
        Kind = kind;
        Value = value;
        Signal = signal;
    }

    /// <summary>Extracts the result as <typeparamref name="T"/>, converting
    /// from the VM's uniform long representation as needed.</summary>
    public T? GetValue<T>() {
        if (!HasValue) return default!;
        if (Value is T t) return t;
        if (Value is long l) {
            if (typeof(T) == typeof(bool)) return (T)(object)(l != 0L);
            if (typeof(T) == typeof(int)) return (T)(object)(int)l;
            if (typeof(T) == typeof(short)) return (T)(object)(short)l;
            if (typeof(T) == typeof(byte)) return (T)(object)(byte)l;
        }
        if (typeof(T) == typeof(object)) return (T)Value!;
        return (T)Convert.ChangeType(Value, typeof(T))!;
    }

    public static InterpreterResult Void => new(ResultKind.Void, null);
    public static InterpreterResult None => Void;
    public static InterpreterResult Return(object? value) => FromSignal(InterpreterSignal.Return(value));
    public static InterpreterResult Break(string? label = null) => FromSignal(InterpreterSignal.Break(label));
    public static InterpreterResult Continue(string? label = null) => FromSignal(InterpreterSignal.Continue(label));
    public static InterpreterResult Throw(Exception exception) => FromSignal(InterpreterSignal.Throw(exception));
    public static InterpreterResult Suspend(string? reason = null) => FromSignal(InterpreterSignal.Suspend(reason));
    public static InterpreterResult FromValue(object? value) => new(ResultKind.Value, value);
    public static InterpreterResult FromSignal(InterpreterSignal signal) => new(
        signal.Kind switch {
            InterpreterSignal.SignalKind.Return => ResultKind.Return,
            InterpreterSignal.SignalKind.Break => ResultKind.Break,
            InterpreterSignal.SignalKind.Continue => ResultKind.Continue,
            InterpreterSignal.SignalKind.Throw => ResultKind.Throw,
            InterpreterSignal.SignalKind.Suspend => ResultKind.Suspend,
            _ => ResultKind.Void
        },
        signal.Value,
        signal);
}

public readonly record struct InterpreterSignal {
    public enum SignalKind { Return, Break, Continue, Throw, Suspend }

    public SignalKind Kind { get; }
    public object? Value { get; }
    public string? Label { get; }

    public static InterpreterSignal Return(object? value = null) =>
        new(SignalKind.Return, value);

    public static InterpreterSignal Break(string? label = null) =>
        new(SignalKind.Break, label: label);

    public static InterpreterSignal Continue(string? label = null) =>
        new(SignalKind.Continue, label: label);

    public static InterpreterSignal Throw(Exception exception) =>
        new(SignalKind.Throw, exception);

    public static InterpreterSignal Suspend(string? reason = null) =>
        new(SignalKind.Suspend, reason);

    private InterpreterSignal(SignalKind kind, object? value = null, string? label = null) {
        Kind = kind;
        Value = value;
        Label = label;
    }
}