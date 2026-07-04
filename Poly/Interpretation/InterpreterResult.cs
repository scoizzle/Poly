namespace Poly.Interpretation;

public readonly record struct InterpreterResult {
    public enum ResultKind { Void, Return, Break, Continue, Throw, Value, Suspend }

    public ResultKind Kind { get; }
    public object? Value { get; }
    public string? Label { get; }

    public bool IsVoid => Kind == ResultKind.Void;
    public bool HasValue => Kind == ResultKind.Value;
    public bool IsSignal => Kind is ResultKind.Return or ResultKind.Break or ResultKind.Continue or ResultKind.Throw or ResultKind.Suspend;

    private InterpreterResult(ResultKind kind, object? value, string? label = null) {
        Kind = kind;
        Value = value;
        Label = label;
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
    public static InterpreterResult Return(object? value = null) => new(ResultKind.Return, value);
    public static InterpreterResult Break(string? label = null) => new(ResultKind.Break, null, label);
    public static InterpreterResult Continue(string? label = null) => new(ResultKind.Continue, null, label);
    public static InterpreterResult Throw(Exception exception) => new(ResultKind.Throw, exception);
    public static InterpreterResult Suspend(string? reason = null) => new(ResultKind.Suspend, reason);
    public static InterpreterResult FromValue(object? value) => new(ResultKind.Value, value);
}