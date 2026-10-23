using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;

using Microsoft.CodeAnalysis.CSharp;

using Poly.Interpretation;
using Poly.Interpretation.CSharp;
using Poly.Interpretation.Vm;

namespace Poly.Tests.Interpretation;

/// <summary>Which way one side of the differential ended: it produced an outcome, or it refused the program.</summary>
public enum ParityDisposal {
    /// <summary>The side accepted the program and produced an outcome (a value, void, a signal, or a throw).</summary>
    Executed,
    /// <summary>The side refused the program: the VM rejected it, the writer would not print it, or Roslyn would not compile the printed text.</summary>
    Rejected,
}

/// <summary>
/// One differential row: the same program put to the VM and to its printed C#, with what each side did.
/// A row agrees when both sides executed to the same outcome, or when both refused the program.
/// </summary>
/// <param name="Program">Which program this is — the corpus label, or a name the test chose.</param>
/// <param name="Vm">How the VM disposed of the program.</param>
/// <param name="VmOutcome">The VM's value, void, signal, throw, or rejection reason.</param>
/// <param name="Printed">How the printed-C# side disposed of the program.</param>
/// <param name="PrintedOutcome">The printed program's value, throw, or refusal reason.</param>
/// <param name="Source">The printed C# text, when the writer got far enough to produce it.</param>
public sealed record ParityRow(
    string Program,
    ParityDisposal Vm,
    string VmOutcome,
    ParityDisposal Printed,
    string PrintedOutcome,
    string? Source) {

    /// <summary>Both sides refused, so there is no execution to compare: the pair is a recorded rejection, not a skip.</summary>
    public bool RejectedOnBothSides => Vm == ParityDisposal.Rejected && Printed == ParityDisposal.Rejected;

    /// <summary>True when the two sides did the same thing to the same program — including both refusing it.</summary>
    public bool Agrees => RejectedOnBothSides
        || (Vm == ParityDisposal.Executed && Printed == ParityDisposal.Executed && VmOutcome == PrintedOutcome);

    /// <summary>The row as a failure message: what each side did, and the source the writer emitted.</summary>
    public string Describe() {
        var lines = new List<string> {
            $"the printed C# does not agree with the VM for '{Program}'",
            $"  VM:      {Vm} — {VmOutcome}",
            $"  printed: {Printed} — {PrintedOutcome}",
        };
        if (Source is not null) {
            lines.Add("  printed source:");
            lines.Add(Source);
        }
        return string.Join("\n", lines);
    }
}

/// <summary>
/// One program the printed C# does not agree with the VM on today, with the defect it records.
/// <para>
/// A divergence is a finding, not a skip: the row still runs both sides and both disposals are
/// pinned here. If either side changes — the VM starts refusing, the writer starts emitting
/// different text, Roslyn finds a different error — the row stops matching and the suite says so.
/// The entry is removed when the defect is fixed, never before.
/// </para>
/// </summary>
/// <param name="Where">The corpus label: the test or test helper that builds the program.</param>
/// <param name="Vm">How the VM disposes of the program.</param>
/// <param name="VmOutcome">The VM's outcome, exactly.</param>
/// <param name="Printed">How the printed side disposes of the program.</param>
/// <param name="PrintedOutcomePrefix">The start of the printed outcome: the value it produced, or the Roslyn diagnostic that refused it.</param>
/// <param name="Finding">The defect class this row is evidence for.</param>
public sealed record KnownDivergence(
    string Where,
    ParityDisposal Vm,
    string VmOutcome,
    ParityDisposal Printed,
    string PrintedOutcomePrefix,
    string Finding) {

    /// <summary>True when <paramref name="row"/> is this recorded divergence, disposition for disposition.</summary>
    public bool Matches(ParityRow row) =>
        row.Program == Where
        && row.Vm == Vm
        && row.VmOutcome == VmOutcome
        && row.Printed == Printed
        && row.PrintedOutcome.StartsWith(PrintedOutcomePrefix, StringComparison.Ordinal);
}

/// <summary>
/// The AST differential: one meaning-source (the Poly AST), checked by a compiler that is not ours.
/// <para>
/// The VM executes the program; <see cref="CSharpWriter"/> prints the same tree through the same
/// analysis, Roslyn compiles the printed text, and the compiled method is executed with the same
/// seed arguments. The row records what each side did, and the two are compared. This replaces a
/// second in-house interpreter, which was an unsound oracle in both directions: it had fewer arms
/// than the VM, it accepted kinds the VM rejects, and it modelled <c>this</c> as null.
/// </para>
/// <para>
/// A refusal is a row, never a skip. When the VM rejects, the printed side must reject too; when
/// the VM executes, the printed side must execute to the same outcome. A difference in which side
/// refuses is a failure. The only exceptions are the programs pinned in
/// <see cref="KnownDivergences"/> — defects already found and recorded, still asserted on both sides.
/// </para>
/// </summary>
public static class AstParity {
    /// <summary>The type and entry point the printed program is wrapped in. The method is a fixture, not language meaning.</summary>
    public const string HarnessTypeName = "PolyParityHarness";
    public const string EntryPointName = "Run";

    /// <summary>
    /// The corpus programs whose printed C# does not agree with the VM, each pinned with the defect
    /// it records. Empty, and it stays empty: IS2d fixed all eleven classes the differential found
    /// (#279), so an entry here is now a writer defect to fix rather than a way to keep a red row
    /// green.
    /// </summary>
    public static IReadOnlyList<KnownDivergence> KnownDivergences { get; } = [];

    static readonly ConcurrentDictionary<string, (ParityDisposal Disposal, string Outcome)> PrintedBySource =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Runs both sides of one program and returns the row. Never throws for a semantic difference —
    /// an asymmetry is data here and a failure in <see cref="AssertAgrees"/>.
    /// </summary>
    /// <param name="program">The corpus label, or a name the caller chose for the row.</param>
    /// <param name="node">The program: an expression, a block, a statement, or a root lambda.</param>
    /// <param name="args">Arguments the VM is seeded with; a root lambda's parameters are bound from them.</param>
    public static ParityRow Compare(string program, Node node, params object?[] args) {
        ArgumentNullException.ThrowIfNull(node);
        // A bare lambda is a program with parameters, not a value: both sides invoke it, the same
        // shape the Invoke-of-lambda entry arm uses.
        var vm = node is Lambda lambda
            ? RunVm(Invoked(lambda, args), [])
            : RunVm(node, args);
        var printed = RunPrinted(node, args);
        return new(program, vm.Disposal, vm.Outcome, printed.Disposal, printed.Outcome, printed.Source);
    }

    static Invoke Invoked(Lambda lambda, object?[] args) =>
        new(lambda, [.. args.Select(static argument => (Node)new Constant(argument))]);

    /// <summary>Runs both sides and fails with the emitted source when they disagree.</summary>
    public static async Task<ParityRow> AssertAgrees(string program, Node node, params object?[] args) {
        var row = Compare(program, node, args);
        await Assert.That(row.Agrees).IsTrue().Because(row.Describe());
        return row;
    }

    /// <summary>
    /// Checks one corpus program at the call site that builds it. Every program the suite hands to
    /// the LINQ oracle is a parity row: it agrees, or it is a <see cref="KnownDivergence"/>. Nothing
    /// else passes — a program with no verdict fails here, and a listed divergence that starts
    /// agreeing fails here too, so the list can only shrink by deleting its entry.
    /// </summary>
    public static void CheckCorpus(Node node) {
        var label = CorpusLabel();
        var row = Compare(label, node);
        if (row.Agrees)
            return;
        if (KnownDivergences.Any(d => d.Matches(row)))
            return;
        throw new InvalidOperationException(
            $"{row.Describe()}\nthe VM and the printed C# must dispose of a corpus program the same way; "
            + $"if this is a new writer defect, add a {nameof(KnownDivergence)} naming it.");
    }

    /// <summary>
    /// The VM's disposal of <paramref name="node"/>: the value it produced, the throw it raised while
    /// running, or the refusal from the compile door. A throw is an outcome both sides can share.
    /// </summary>
    public static (ParityDisposal Disposal, string Outcome) RunVm(Node node, params object?[] args) {
        VmProgram program;
        try {
            program = Interpreter.Compile(node);
        }
        catch (Exception ex) {
            return (ParityDisposal.Rejected, $"{ex.GetType().Name}: {ex.Message}");
        }

        try {
            using var exec = Interpreter.Execute(
                program,
                state => {
                    state.MaxLoopIterations = 100_000_000;
                    state.SetArgs(args);
                });
            return (ParityDisposal.Executed, DescribeVmResult(exec));
        }
        catch (Exception ex) {
            return (ParityDisposal.Executed, $"throw {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// The printed side's disposal: writer → Roslyn syntax → Roslyn compile → execute. A writer
    /// refusal, a syntax error, and a compile error are all "rejected", each carrying the reason and,
    /// when there is one, the source. A throw from the compiled body is an outcome, not a rejection.
    /// </summary>
    public static (ParityDisposal Disposal, string Outcome, string? Source) RunPrinted(Node node, params object?[] args) {
        string source;
        try {
            source = Print(node);
        }
        catch (InvalidOperationException ex) {
            return (ParityDisposal.Rejected, "writer refused: " + ex.Message, null);
        }

        var syntaxErrors = SyntaxErrors(source);
        if (syntaxErrors.Count > 0)
            return (ParityDisposal.Rejected, "Roslyn syntax errors: " + string.Join("; ", syntaxErrors), source);

        if (!PrintedBySource.TryGetValue(source, out var compiled)) {
            compiled = Execute(source, args);
            PrintedBySource[source] = compiled;
        }
        return (compiled.Disposal, compiled.Outcome, source);
    }

    /// <summary>Prints the program as a method body, using the analysis the VM itself compiled.</summary>
    public static string Print(Node node) {
        AnalysisResult? analysis;
        try {
            analysis = Interpreter.Analyze(node);
        }
        catch (Exception) {
            analysis = null;
        }

        var (returnType, body, parameters) = EntryShape(node);
        var typeDef = new TypeDefinitionNode(HarnessTypeName, Methods: [
            new MethodDefinitionNode(
                EntryPointName,
                returnType,
                Parameters: parameters,
                Body: body,
                IsStatic: true)
        ]);
        var generator = analysis is null ? new CSharpGenerator() : new CSharpGenerator(analysis);
        return generator.Generate([typeDef]);
    }

    /// <summary>
    /// The printed entry point for a program: a value expression returns its value, a block keeps its
    /// statements and returns the last expression (the meaning <see cref="Block"/> carries), a root
    /// lambda becomes the method's parameters and body, and any other statement is the body itself.
    /// </summary>
    private static (Node ReturnType, Node Body, IReadOnlyList<Parameter>? Parameters) EntryShape(Node node) {
        var objectType = new ClrTypeReference(typeof(object));
        var voidType = new TypeReference("void");
        return node switch {
            Return => (objectType, node, null),
            Comment => (voidType, node, null),
            Block block when Yields(block) => (objectType, Hoisted(block), null),
            Block => (voidType, node, null),
            Lambda lambda => LambdaEntry(lambda),
            Invoke { Delegate: Lambda lambda, Arguments.Length: 0 } => LambdaEntry(lambda),
            Poly.Ast.Nodes.Expression => (objectType, new Return(node), null),
            _ => (voidType, node, null),
        };
    }

    /// <summary>A root lambda's body is the program: a value body becomes the method's return and
    /// a block body keeps its statements with its trailing value returned.</summary>
    static (Node ReturnType, Node Body, IReadOnlyList<Parameter>? Parameters) LambdaEntry(Lambda lambda) =>
        lambda.Body switch {
            Block block when Yields(block) => (new ClrTypeReference(typeof(object)), Hoisted(block), lambda.Parameters),
            Block => (new TypeReference("void"), lambda.Body, lambda.Parameters),
            _ => (new ClrTypeReference(typeof(object)), new Return(lambda.Body), lambda.Parameters),
        };

    /// <summary>True when the block's value reaches a consumer: its last node is a value
    /// expression, or a block that is one. The value is the block's meaning, so the entry point
    /// returns it.</summary>
    static bool Yields(Block block) => block.Nodes.Count > 0 && block.Nodes[^1] switch {
        Block inner => Yields(inner),
        Poly.Ast.Nodes.Expression => true,
        _ => false,
    };

    /// <summary>
    /// Rewrites a value-carrying block so its trailing value is a <c>return</c> in the block that
    /// carries it. C# has no block expression, so the consumer has to name the value: the writer
    /// prints a block as a statement list and cannot invent a value its consumer never asked for.
    /// </summary>
    static Block Hoisted(Block block) =>
        block.Nodes[^1] is Block inner
            ? new Block([.. block.Nodes.Take(block.Nodes.Count - 1), Hoisted(inner)], block.Variables)
            : new Block([.. block.Nodes.Take(block.Nodes.Count - 1), new Return(block.Nodes[^1])], block.Variables);

    static (ParityDisposal Disposal, string Outcome) Execute(string source, object?[] args) {
        Assembly assembly;
        try {
            assembly = ExportedCSharp.CompileAndLoad(source, HarnessTypeName);
        }
        catch (InvalidOperationException ex) {
            return (ParityDisposal.Rejected, "Roslyn compile errors: " + ex.Message);
        }

        try {
            var entry = assembly.GetType(HarnessTypeName)?.GetMethod(EntryPointName, BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException($"No {HarnessTypeName}.{EntryPointName} in the printed program.");
            var value = entry.Invoke(null, BindArguments(entry.GetParameters(), args));
            return (ParityDisposal.Executed, entry.ReturnType == typeof(void) ? "void" : Canonical(value, declared: null));
        }
        catch (Exception ex) {
            var thrown = ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
            return (ParityDisposal.Executed, $"throw {thrown.GetType().Name}: {thrown.Message}");
        }
    }

    /// <summary>Reflection arguments for the printed entry point: the seed values, converted to the parameter types.</summary>
    private static object?[] BindArguments(ParameterInfo[] parameters, object?[] args) {
        if (parameters.Length != args.Length)
            throw new InvalidOperationException(
                $"the printed entry point takes {parameters.Length} argument(s) and the row supplied {args.Length}; entry shape and seed disagree");
        var bound = new object?[args.Length];
        for (var i = 0; i < args.Length; i++)
            bound[i] = args[i] is null ? null : Convert.ChangeType(args[i], parameters[i].ParameterType, CultureInfo.InvariantCulture);
        return bound;
    }

    /// <summary>The VM's result as one canonical string, so a value, a signal, and a throw are all comparable to the printed side.</summary>
    private static string DescribeVmResult(ExecutionResult exec) {
        var clr = exec.State.Program.RootClrType;
        return exec.Result.Kind switch {
            InterpreterResult.ResultKind.Void => "void",
            InterpreterResult.ResultKind.Value => Canonical(exec.Result.Value, clr),
            InterpreterResult.ResultKind.Return => Canonical(exec.Result.Value, clr),
            InterpreterResult.ResultKind.Throw => $"throw {exec.Result.Value?.GetType().Name}: {(exec.Result.Value as Exception)?.Message}",
            InterpreterResult.ResultKind.Suspend => "suspend",
            InterpreterResult.ResultKind.Break => "break",
            InterpreterResult.ResultKind.Continue => "continue",
            _ => exec.Result.Kind.ToString(),
        };
    }

    /// <summary>
    /// One spelling for a value from either side: the CLR type kind and the invariant text, so an int
    /// the VM keeps as a raw word and the printed side boxes compare equal, while a difference in type
    /// (long 8 vs double 8) does not.
    /// </summary>
    private static string Canonical(object? value, Type? declared) {
        if (declared == typeof(bool) && value is long flag)
            return "bool:" + (flag != 0L ? "true" : "false");
        return value switch {
            null => "null",
            bool b => "bool:" + (b ? "true" : "false"),
            string s => "string:" + s,
            double d => "double:" + d.ToString("R", CultureInfo.InvariantCulture),
            float f => "double:" + ((double)f).ToString("R", CultureInfo.InvariantCulture),
            long l => "long:" + l.ToString(CultureInfo.InvariantCulture),
            ulong ul => "long:" + unchecked((long)ul).ToString(CultureInfo.InvariantCulture),
            int or short or sbyte or byte or ushort or uint =>
                "long:" + Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            char c => "char:" + c,
            _ => $"{value.GetType().Name}:{Convert.ToString(value, CultureInfo.InvariantCulture)}",
        };
    }

    /// <summary>Roslyn's syntax errors for a printed program; empty means the text is a well-formed compilation unit.</summary>
    public static IReadOnlyList<string> SyntaxErrors(string source) =>
        [.. CSharpSyntaxTree.ParseText(source)
            .GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(d => $"{d.Id} {d.GetMessage()}")];

    /// <summary>The test or test helper that built the program being checked: <c>Type.Method</c>.</summary>
    static string CorpusLabel() {
        foreach (var line in Environment.StackTrace.Split('\n')) {
            var frame = line.Trim();
            if (!frame.StartsWith("at Poly.Tests.", StringComparison.Ordinal)
                || frame.Contains(nameof(AstParity), StringComparison.Ordinal)
                || frame.Contains(nameof(NodeTestHelpers), StringComparison.Ordinal))
                continue;
            var signature = frame[3..].Split('(')[0].Trim();
            var plus = signature.IndexOf('+');
            if (plus > 0 && signature.Length > plus + 2 && signature[plus + 1] == '<') {
                var close = signature.IndexOf('>', plus + 1);
                if (close > plus)
                    signature = signature[..plus] + "." + signature[(plus + 2)..close];
            }
            var typeName = signature[..signature.LastIndexOf('.')];
            var methodName = signature[(signature.LastIndexOf('.') + 1)..];
            var bracket = methodName.IndexOf('[');
            if (bracket > 0)
                methodName = methodName[..bracket];
            return $"{typeName[(typeName.LastIndexOf('.') + 1)..]}.{methodName}";
        }
        return "unknown";
    }
}