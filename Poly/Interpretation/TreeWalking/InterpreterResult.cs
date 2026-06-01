using System;

using Poly.Syntax;
using Poly.Syntax;
using Poly.Syntax.Analysis;

namespace Poly.Interpretation.TreeWalking;

/// <summary>
/// Result of evaluating a Syntax.Node in the tree-walking VM.
/// Uses a simple discriminated union pattern for clarity and performance.
/// </summary>
public readonly record struct InterpreterResult {
    public static InterpreterResult None => default;
    public static InterpreterResult FromValue(object? value) => new(value, null);
    public static InterpreterResult FromSignal(InterpreterSignal signal) => new(null, signal);

    public object? Value { get; }
    public InterpreterSignal? Signal { get; }

    public bool IsVoid => Signal is null && Value is null;
    public bool HasValue => Signal is null && Value is not null;
    public bool IsSignal => Signal is not null;

    private InterpreterResult(object? value, InterpreterSignal? signal) {
        Value = value;
        Signal = signal;
    }
}

/// <summary>
/// Represents non-local control flow within the interpreter.
/// Used instead of exceptions for structured control flow (return, break, continue).
/// </summary>
public readonly record struct InterpreterSignal {
    public enum SignalKind { Return, Break, Continue, Throw }

    public SignalKind Kind { get; }
    public object? Value { get; }        // return value or thrown exception
    public string? Label { get; }        // for labeled break/continue (future)

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