using System;

using Poly.Syntax.Analysis;

namespace Poly.Interpretation.TreeWalking;

/// <summary>
/// Result of evaluating a Syntax.Node in the tree-walking VM.
/// Uses a simple discriminated union pattern for clarity and performance.
/// </summary>
public readonly record struct InterpreterResult {
    public enum ResultKind { Void, Return, Break, Continue, Throw, Value }

    public ResultKind Kind { get; }
    public object? Value { get; }
    public InterpreterSignal? Signal { get; }

    public bool IsVoid => Kind == ResultKind.Void;
    public bool HasValue => Kind == ResultKind.Value;
    public bool IsSignal => Kind is ResultKind.Return or ResultKind.Break or ResultKind.Continue or ResultKind.Throw;
    public string? Label => Signal?.Label;

    private InterpreterResult(ResultKind kind, object? value, InterpreterSignal? signal = null) {
        Kind = kind;
        Value = value;
        Signal = signal;
    }

    public static InterpreterResult Void => new(ResultKind.Void, null);
    public static InterpreterResult None => Void;
    public static InterpreterResult Return(object? value) => FromSignal(InterpreterSignal.Return(value));
    public static InterpreterResult Break(string? label = null) => FromSignal(InterpreterSignal.Break(label));
    public static InterpreterResult Continue(string? label = null) => FromSignal(InterpreterSignal.Continue(label));
    public static InterpreterResult Throw(Exception exception) => FromSignal(InterpreterSignal.Throw(exception));
    public static InterpreterResult FromValue(object? value) => new(ResultKind.Value, value);
    public static InterpreterResult FromSignal(InterpreterSignal signal) => new(
        signal.Kind switch {
            InterpreterSignal.SignalKind.Return => ResultKind.Return,
            InterpreterSignal.SignalKind.Break => ResultKind.Break,
            InterpreterSignal.SignalKind.Continue => ResultKind.Continue,
            InterpreterSignal.SignalKind.Throw => ResultKind.Throw,
            _ => ResultKind.Void
        },
        signal.Value,
        signal);
}

/// <summary>
/// Represents non-local control flow within the interpreter.
/// Used instead of exceptions for structured control flow (return, break, continue).
/// </summary>
public readonly record struct InterpreterSignal {
    public enum SignalKind { Return, Break, Continue, Throw }

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

    private InterpreterSignal(SignalKind kind, object? value = null, string? label = null) {
        Kind = kind;
        Value = value;
        Label = label;
    }
}