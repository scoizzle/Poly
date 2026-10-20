using System.Globalization;

using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm;

namespace Poly.Interpretation.CSharp;

/// <summary>
/// Runs a C#-subset snippet on the Poly VM: reads the text with
/// <see cref="CSharpReader"/>, analyzes and compiles it through
/// <see cref="Interpreter"/>, and either executes it (<see cref="Run"/>) or hands
/// back a stepping session over <see cref="VmDebugger"/> (<see cref="ScriptDebugSession"/>).
/// The engine behind the Poly console; it adds no language semantics of its own.
/// </summary>
public static class CSharpScript {
    /// <summary>Loop-iteration cap applied to a run or debug session unless overridden.</summary>
    public const long DefaultMaxLoopIterations = 1_000_000;

    /// <summary>Outcome of <see cref="Run"/>: the produced value, or the diagnostics that blocked it.</summary>
    /// <param name="Success">True when the snippet was read, compiled, and executed.</param>
    /// <param name="Diagnostics">Reader/analyzer/execution failures, formatted for display. Empty on success.</param>
    /// <param name="Value">Rendered result value, <c>(void)</c>, or <c>(suspended)</c>. Null on failure.</param>
    /// <param name="ValueType">CLR type name of the value when one was produced; null otherwise.</param>
    public sealed record ScriptResult(
        bool Success,
        IReadOnlyList<string> Diagnostics,
        string? Value,
        string? ValueType);

    /// <summary>
    /// Reads, analyzes, compiles, and executes <paramref name="snippet"/>. Never
    /// throws: reader, analyzer, and execution failures come back as
    /// <see cref="ScriptResult.Diagnostics"/>.
    /// </summary>
    public static ScriptResult Run(string snippet, long maxLoopIterations = DefaultMaxLoopIterations) {
        try {
            var unit = ReadUnit(snippet);
            var node = ProgramBlock(unit);
            var analysis = Interpreter.Analyze(unit);
            var errors = Errors(analysis);
            if (errors.Count > 0)
                return new ScriptResult(false, errors, null, null);

            var program = Interpreter.Compile(node, analysis, CompilationMode.Normal);
            using var exec = Interpreter.Execute(program, state => state.MaxLoopIterations = maxLoopIterations);
            if (exec.IsSuspended)
                return new ScriptResult(true, [], "(suspended)", null);
            if (!exec.HasValue)
                return new ScriptResult(true, [], "(void)", null);
            // A boolean-typed root reaches Result.Value as a raw word (0/non-zero);
            // render it honestly. Other values are read off Result.Value, not
            // RawValue: a double result is boxed there, where RawValue would
            // expose its bit pattern.
            return exec.State.Program.RootClrType == typeof(bool)
                ? new ScriptResult(true, [], exec.Result.Value is long flag && flag != 0L ? "true" : "false", nameof(Boolean))
                : Describe(exec.Result.Value);
        }
        catch (Exception ex) {
            return new ScriptResult(false, [Describe(ex)], null, null);
        }
    }

    /// <summary>
    /// Normalizes <paramref name="snippet"/> into a single runnable node. The
    /// reader only records <c>var</c> locals for declarations directly inside a
    /// block, so the snippet is wrapped in <c>{ … }</c>; a missing terminator is
    /// added unless the snippet already ends in <c>}</c>. A snippet ending in
    /// <c>}</c> is taken as a complete block and gets no trailing terminator, so
    /// an initializer or lambda whose text ends in <c>}</c> must supply its own
    /// <c>;</c>. Throws <see cref="FormatException"/> on empty or unsupported input.
    /// </summary>
    public static Node ReadProgram(string snippet) => ProgramBlock(ReadUnit(snippet));

    /// <summary>The parsed snippet as one compilation unit: the wrapper block is
    /// the top-level statement and any type declarations stay in <c>Types</c>, so
    /// analysis walks them with the block. Emit compiles the block.</summary>
    internal static CompilationUnitNode ReadUnit(string snippet) {
        ArgumentNullException.ThrowIfNull(snippet);
        var body = snippet.Trim();
        if (body.Length == 0)
            throw new FormatException("Snippet is empty.");
        if (!body.EndsWith('}') && !body.EndsWith(';'))
            body += ";";

        return CSharpLanguage.Read("{ " + body + " }");
    }

    internal static Node ProgramBlock(CompilationUnitNode unit) {
        var statements = unit.TopLevelStatements;
        if (statements is null || statements.Count == 0)
            throw new FormatException(unit.Types.Count > 0
                ? "Only type declarations were found; there is nothing to run."
                : "Nothing to run.");
        // The wrapper guarantees a single block statement, so this is the only
        // branch in practice; the fallback is defensive and carries no declared
        // locals (a bare declaration outside a block never declares one anyway).
        return statements.Count == 1 && statements[0] is Block block
            ? block
            : new Block(statements, Array.Empty<Node>());
    }

    internal static List<string> Errors(AnalysisResult analysis) =>
        analysis.Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.Code is null
                ? diagnostic.Message
                : $"{diagnostic.Code}: {diagnostic.Message}")
            .ToList();

    private static ScriptResult Describe(object? value) => value switch {
        null => new ScriptResult(true, [], "null", null),
        string text => new ScriptResult(true, [], $"\"{text}\"", nameof(String)),
        bool flag => new ScriptResult(true, [], flag ? "true" : "false", nameof(Boolean)),
        _ => new ScriptResult(
            true,
            [],
            Convert.ToString(value, CultureInfo.InvariantCulture) ?? value.ToString() ?? "?",
            value.GetType().Name),
    };

    private static string Describe(Exception exception) => $"{exception.GetType().Name}: {exception.Message}";
}

/// <summary>
/// A single stepping session over a compiled snippet. Owns the
/// <see cref="VmDebugger"/>; at most one operation may be in flight. The loop cap
/// is applied to the debugger's own state, so a runaway snippet under
/// <see cref="ContinueAsync"/> still terminates. <see cref="Dispose"/> cancels and
/// awaits an in-flight step before tearing the debugger down.
/// </summary>
public sealed class ScriptDebugSession : IDisposable {
    private readonly VmDebugger _debugger;
    private readonly CancellationTokenSource _cts = new();
    private Task<DebugResult>? _inFlight;
    private bool _disposed;

    private ScriptDebugSession(VmDebugger debugger) => _debugger = debugger;

    /// <summary>Current AST node rendered as C# (falls back to the node's type name if it cannot be printed).</summary>
    public string CurrentNodeText { get; private set; } = string.Empty;

    /// <summary>Named locals at the last statement boundary. Numeric values only.</summary>
    public IReadOnlyList<(string Name, long Value)> Locals { get; private set; } = [];

    /// <summary>True once the program has run to completion (or faulted).</summary>
    public bool IsCompleted => _debugger.IsCompleted;

    /// <summary>Exception thrown by the snippet, if any.</summary>
    public Exception? Fault => _debugger.ExecutionException;

    /// <summary>True while a step or continue is running.</summary>
    public bool IsBusy => _inFlight is { IsCompleted: false };

    /// <summary>
    /// Starts debugging <paramref name="snippet"/>, pausing at the first statement
    /// boundary. On failure returns null and fills <paramref name="diagnostics"/>.
    /// </summary>
    public static ScriptDebugSession? TryStart(
        string snippet,
        out IReadOnlyList<string> diagnostics,
        long maxLoopIterations = CSharpScript.DefaultMaxLoopIterations) {
        diagnostics = [];
        try {
            var unit = CSharpScript.ReadUnit(snippet);
            var node = CSharpScript.ProgramBlock(unit);
            var analysis = Interpreter.Analyze(unit);
            var errors = CSharpScript.Errors(analysis);
            if (errors.Count > 0) {
                diagnostics = errors;
                return null;
            }

            var program = Interpreter.Compile(node, analysis, CompilationMode.Normal);
            var debugger = new VmDebugger(program);
            // The debugger owns its VmState; the cap must be set before Start, or a
            // runaway snippet under Continue can never be stopped.
            debugger.State.MaxLoopIterations = maxLoopIterations;
            var session = new ScriptDebugSession(debugger);
            session.Apply(debugger.Start(session._cts.Token));
            return session;
        }
        catch (Exception ex) {
            diagnostics = [$"{ex.GetType().Name}: {ex.Message}"];
            return null;
        }
    }

    /// <summary>Advances one statement boundary.</summary>
    public Task StepAsync() => AdvanceAsync(step: true);

    /// <summary>Runs to completion or until a suspension.</summary>
    public Task ContinueAsync() => AdvanceAsync(step: false);

    private async Task AdvanceAsync(bool step) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsBusy)
            throw new InvalidOperationException("A step is already in flight.");

        var token = _cts.Token;
        var task = Task.Run(() => step ? _debugger.StepOver(token) : _debugger.Continue(token), token);
        _inFlight = task;
        Apply(await task);
    }

    private void Apply(DebugResult result) {
        Locals = result.Locals;
        try {
            CurrentNodeText = CSharpLanguage.Write(result.Node);
        }
        catch {
            CurrentNodeText = result.Node.GetType().Name;
        }
    }

    public void Dispose() {
        if (_disposed)
            return;
        _disposed = true;
        _cts.Cancel();
        if (_inFlight is { } pending) {
            try {
                pending.GetAwaiter().GetResult();
            }
            catch {
                // Cancelled or faulted step: the session is going away either way.
            }
        }
        // VmDebugger.Dispose cannot stop a program that is not blocked in the
        // hook, so a stopped session would otherwise keep running on a pool
        // thread to the cap. Pull the sandbox limit down to the ticks already
        // spent so the delegate self-terminates at its next loop check.
        _debugger.State.MaxLoopIterations = _debugger.State.LoopTicks;
        _debugger.Dispose();
        _cts.Dispose();
    }
}