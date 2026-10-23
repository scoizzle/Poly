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
/// seed arguments. The row records what each side did, and the two are compared. This replaces the
/// assumption that <c>LinqExpressionGenerator</c> is a sound oracle: it has fewer arms than the VM,
/// accepts kinds the VM rejects, and models <c>this</c> as null — so it cannot show these.
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
    /// The corpus programs whose printed C# does not agree with the VM today. Every entry is a
    /// writer defect the differential found; each names its class so the list reads as findings.
    /// </summary>
    public static IReadOnlyList<KnownDivergence> KnownDivergences { get; } = [
        new("ArithmeticParserEvaluatorTests.MixedTypes_ComplexExpression_PromotesCorrectly", ParityDisposal.Executed, "double:22.5",
            ParityDisposal.Executed, "double:23",
            "double constant: the printed subtraction loses a double operand and rounds to an integer"),
        new("ArithmeticParserEvaluatorTests.UnaryMinus_WithParentheses_EvaluatesCorrectly", ParityDisposal.Executed, "long:-15",
            ParityDisposal.Executed, "long:-5",
            "unary minus: a negated negative constant is one operand with the wrong value"),
        new("ArithmeticParserEvaluatorTests.InvalidOperation_DivideByZero_ThrowsException",
            ParityDisposal.Executed, "throw DivideByZeroException: Attempted to divide by zero.",
            ParityDisposal.Rejected, "Roslyn compile errors:",
            "constant division by zero: the VM throws at runtime, the printed `x / 0` is CS0020 at compile time"),
        new("ArithmeticNodeTests.Divide_TwoDoubles_ReturnsDecimalQuotient", ParityDisposal.Executed, "double:3.5",
            ParityDisposal.Executed, "long:3",
            "double constant: a whole-valued double prints without a decimal point, so `/` becomes integer division"),
        new("BlockScopeTests.BlockScope_NestedScopes_InnerShadowsOuter", ParityDisposal.Executed, "long:50",
            ParityDisposal.Rejected, "Roslyn compile errors:",
            "block shadowing: an inner block may shadow an outer local, which C# forbids (CS0136)"),
        new("BlockScopeTests.BlockScope_CreatesNewScope_VariablesNotVisibleOutside", ParityDisposal.Executed, "long:50",
            ParityDisposal.Rejected, "Roslyn compile errors:",
            "block shadowing: an inner block may shadow an outer local, which C# forbids (CS0136)"),
        new("BlockScopeTests.BlockScope_MultipleBlocks_IndependentScopes", ParityDisposal.Executed, "long:20",
            ParityDisposal.Rejected, "Roslyn compile errors:",
            "block value: a block's trailing expression prints as a bare expression statement (CS0201)"),
        new("CoalesceTests.Coalesce_WithNonNullLeft_ReturnsLeftValue", ParityDisposal.Executed, "long:100",
            ParityDisposal.Rejected, "Roslyn compile errors:",
            "coalesce: `??` is printed on a non-nullable operand, which C# rejects (CS0019)"),
        new("CoalesceTests.Coalesce_WithNullLeft_ReturnsRightValue", ParityDisposal.Executed, "long:42",
            ParityDisposal.Rejected, "Roslyn compile errors:",
            "coalesce: `??` is printed on a non-nullable operand, which C# rejects (CS0019)"),
        new("CoalesceTests.Coalesce_ChainedOperators_WorksCorrectly", ParityDisposal.Executed, "long:10",
            ParityDisposal.Rejected, "Roslyn compile errors:",
            "coalesce: `??` is printed on a non-nullable operand, which C# rejects (CS0019)"),
        new("ConstantNodeTests.Constant_DateTime_CompilesAndExecutes", ParityDisposal.Executed,
            "DateTime:01/15/2024 10:30:00", ParityDisposal.Rejected, "Roslyn syntax errors:",
            "constant fallback: a DateTime constant prints as its ToString, not a C# literal"),
        new("ConstantNodeTests.Constant_ComplexObject_CompilesAndExecutes", ParityDisposal.Executed,
            "List`1:System.Collections.Generic.List`1[System.Int32]", ParityDisposal.Rejected, "Roslyn syntax errors:",
            "constant fallback: a collection constant prints as its type name, not a C# literal"),
        new("ForEachLoopTests.ForEachLoop_Break_StopsIterating", ParityDisposal.Executed, "long:6",
            ParityDisposal.Rejected, "Roslyn syntax errors:",
            "constant fallback: the collection constant prints as `System.Int32[]`"),
        new("ForEachLoopTests.ForEachLoop_Continue_SkipsRemainingBodyForCurrentIteration", ParityDisposal.Executed,
            "long:4", ParityDisposal.Rejected, "Roslyn syntax errors:",
            "constant fallback: the collection constant prints as `System.Int32[]`"),
        new("ForEachLoopTests.ForEachLoop_EmptyCollection_DoesNotExecuteBody", ParityDisposal.Executed, "long:5",
            ParityDisposal.Rejected, "Roslyn syntax errors:",
            "constant fallback: the collection constant prints as `System.Int32[]`"),
        new("ForEachLoopTests.ForEachLoop_ShadowsOuterVariable_WithoutLeakingValue", ParityDisposal.Executed, "long:103",
            ParityDisposal.Rejected, "Roslyn syntax errors:",
            "constant fallback: the collection constant prints as `System.Int32[]`"),
        new("ForEachLoopTests.ForEachLoop_UsesLoopVariableInsideBody", ParityDisposal.Executed, "long:10",
            ParityDisposal.Rejected, "Roslyn syntax errors:",
            "constant fallback: the collection constant prints as `System.Int32[]`"),
        new("InterpreterLanguageGotchaTests.NotEqual_NaN_IsTrue", ParityDisposal.Executed, "bool:true",
            ParityDisposal.Rejected, "Roslyn compile errors:",
            "double constant: `double.NaN` prints as the bare name `NaN` (CS0103)"),
        new("InterpreterLanguageGotchaTests.Equal_NaN_IsFalse", ParityDisposal.Executed, "bool:false",
            ParityDisposal.Rejected, "Roslyn compile errors:",
            "double constant: `double.NaN` prints as the bare name `NaN` (CS0103)"),
        new("InterpreterLanguageGotchaTests.LessThan_NaN_IsFalse", ParityDisposal.Executed, "bool:false",
            ParityDisposal.Rejected, "Roslyn compile errors:",
            "double constant: `double.NaN` prints as the bare name `NaN` (CS0103)"),
        new("InterpreterLanguageGotchaTests.Add_LongOverflow_WrapsUnchecked", ParityDisposal.Executed, "long:-9223372036854775808",
            ParityDisposal.Rejected, "Roslyn compile errors:",
            "checked arithmetic: the VM wraps, the printed constant sum overflows in checked mode (CS0220)"),
        new("LambdaInvokeTests.Invoke_WithLambdaNode_ExecutesBody", ParityDisposal.Executed, "long:15",
            ParityDisposal.Rejected, "Roslyn compile errors:",
            "lambda invocation: a lambda callee prints without the arguments' brackets it needs (CS0149)"),
        new("LambdaInvokeTests.InvokeWith_ExtensionMethod_BuildsInvokeNode", ParityDisposal.Executed, "long:107",
            ParityDisposal.Rejected, "Roslyn compile errors:",
            "lambda invocation: a lambda callee prints without the arguments' brackets it needs (CS0149)"),
        new("LambdaInvokeTests.Lambda_WithNoParameters_CompilesAndReturnsConstant", ParityDisposal.Executed,
            "Object[]:System.Object[]", ParityDisposal.Rejected, "Roslyn compile errors:",
            "lambda root: a bare lambda has no printed value form and prints as a statement (CS0201)"),
        new("NewNodeTests.New_ParameterlessConstructor_CompilesAndCreatesInstance", ParityDisposal.Executed,
            "ParityWidget:Poly.Tests.Interpretation.AstParityTests+ParityWidget", ParityDisposal.Rejected, "Roslyn syntax errors:",
            "nested type name: a nested CLR type prints with the reflection separator `+`, not `.`"),
        new("NewNodeTests.New_WithExplicitArguments_BindsAllConstructorParameters", ParityDisposal.Executed,
            "ParityWidget:Poly.Tests.Interpretation.AstParityTests+ParityWidget", ParityDisposal.Rejected, "Roslyn syntax errors:",
            "nested type name: a nested CLR type prints with the reflection separator `+`, not `.`"),
        new("NewNodeTests.New_WithOptionalParameter_UsesDefaultValueWhenOmitted", ParityDisposal.Executed,
            "ParityWidget:Poly.Tests.Interpretation.AstParityTests+ParityWidget", ParityDisposal.Rejected, "Roslyn syntax errors:",
            "nested type name: a nested CLR type prints with the reflection separator `+`, not `.`"),
        new("TypeCastTests.TypeAs_ObjectToString_WithIncompatibleValue_ReturnsNull", ParityDisposal.Executed, "null",
            ParityDisposal.Rejected, "Roslyn compile errors:",
            "type as: `42 as string` is CS0039, although the VM's `as` is a runtime try-cast that yields null"),
        new("UnaryMinusTests.UnaryMinus_WithNegativeInteger_ReturnsPositive", ParityDisposal.Executed, "long:99",
            ParityDisposal.Rejected, "Roslyn compile errors:",
            "unary minus: a negated negative constant prints as the decrement `--99` (CS1059)"),
    ];

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
        var vm = RunVm(node, args);
        var printed = RunPrinted(node, args);
        return new(program, vm.Disposal, vm.Outcome, printed.Disposal, printed.Outcome, printed.Source);
    }

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
            Block block when block.Nodes.Count > 0 && block.Nodes[^1] is Poly.Ast.Nodes.Expression =>
                (objectType, new Block(
                    [.. block.Nodes.Take(block.Nodes.Count - 1), new Return(block.Nodes[^1])],
                    block.Variables), null),
            Block => (voidType, node, null),
            Lambda lambda => (objectType, lambda, null),
            Invoke { Delegate: Lambda lambda, Arguments.Length: 0 } => (objectType, lambda.Body, lambda.Parameters),
            Poly.Ast.Nodes.Expression => (objectType, new Return(node), null),
            _ => (voidType, node, null),
        };
    }

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