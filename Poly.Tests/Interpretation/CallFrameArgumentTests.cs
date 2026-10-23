using Poly.Ast.Nodes;
using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.CSharp;
using Poly.Interpretation.Vm;
using Poly.Interpretation.Vm.Abi;
using Poly.Introspection;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Interpretation;

/// <summary>
/// A call frame's arguments reach <see cref="VmCall.Invoke"/> as individual words, not as
/// a freshly allocated <c>long[]</c>. The array was the single largest cost of a call —
/// nqueens spends 18 % of its run in it, and it allocated 672 KB per run — while adding
/// nothing the frame protocol needs: <see cref="VmCall"/> already copies each word into
/// its slot.
/// </summary>
public class CallFrameArgumentTests {
    private static AbiEmitter.BuiltVmProgram Build(string source) {
        var unit = CSharpLanguage.Read(source);
        return AbiEmitter.Build(unit.TopLevelStatements![0], Interpreter.Analyze(unit), CompilationMode.NoDebug);
    }

    private static VmTree.Scan Scan(string source) => VmTree.Read(Build(source).Expression);

    private static long Run(string source) {
        var unit = CSharpLanguage.Read(source);
        using var exec = Interpreter.Execute(
            Interpreter.Compile(unit.TopLevelStatements![0], Interpreter.Analyze(unit), CompilationMode.NoDebug));
        return exec.RawValue;
    }

    /// <summary>The row that pays for the change: a call site names its arguments one by
    /// one, so no <c>long[]</c> is materialised for the frame.</summary>
    [Test, Timeout(10_000)]
    public async Task MethodCall_DoesNotAllocateArgumentArray(CancellationToken ct) {
        _ = ct;
        var scan = Scan("""
            class P {
                P() { }
                long Add(long a, long b) { return a + b; }
                long Run(long n) { return this.Add(n, 2); }
            }
            new P().Run(40);
            """);

        var frames = scan.CallFramesWithArgumentArray
            .Select(call => call.ToString()).ToList();
        await Assert.That(frames.Count).IsEqualTo(0)
            .Because("every call frame passes its words directly: " + string.Join(" | ", frames));
        await Assert.That(scan.CallFrames.Count).IsGreaterThan(0)
            .Because("the program does make an AST method call");
    }

    /// <summary>A constructor frame is the same protocol as a method frame, so it takes
    /// the same shape. Written through the AST because the C# surface has no
    /// parameterized constructor.</summary>
    [Test, Timeout(10_000)]
    public async Task ConstructorCall_DoesNotAllocateArgumentArray(CancellationToken ct) {
        _ = ct;
        var typeNode = new TypeDefinitionNode(
            "Widget",
            "Sample",
            Properties: [new PropertyDefinitionNode("Count", new PrimitiveTypeReference(PrimitiveType.Int64))],
            Constructors: [
                new ConstructorDefinitionNode(
                    [
                        new Parameter("name", new PrimitiveTypeReference(PrimitiveType.String)),
                        new Parameter("count", new PrimitiveTypeReference(PrimitiveType.Int64)),
                    ],
                    new Block([new Assignment(new Member(new ThisReference(), "Count"), new Constant(1L))])),
            ]);
        var newNode = new New(new TypeReference("Sample.Widget"), new Constant("alpha"), new Constant(2L));
        var analyzer = new TypeDefinitionNodeAnalyzer();
        var context = AnalysisContext.CreateDefault();
        analyzer.Analyze(context, typeNode);
        var analysis = Interpreter.Analyze(newNode, context.TypeDefinitions);

        var scan = VmTree.Read(AbiEmitter.Build(newNode, analysis, CompilationMode.NoDebug).Expression);

        var frames = scan.CallFramesWithArgumentArray
            .Select(call => call.ToString()).ToList();
        await Assert.That(scan.CallFrames.Count).IsGreaterThan(0)
            .Because("the constructor body is a call frame of its own");
        await Assert.That(frames.Count).IsEqualTo(0)
            .Because("no constructor frame materialises an argument array: " + string.Join(" | ", frames));
    }

    /// <summary>Every parameter reaches the callee, including the receiver, so the words
    /// are in frame order — a dropped or reordered word would change the result.</summary>
    [Test, Timeout(10_000)]
    public async Task MethodCall_PassesEveryArgumentInFrameOrder(CancellationToken ct) {
        _ = ct;
        var result = Run("""
            class P {
                P() { }
                long Pack(long a, long b, long c, long d) { return a + b * 10 + c * 100 + d * 1000; }
                long Run() { return this.Pack(1, 2, 3, 4); }
            }
            new P().Run();
            """);

        await Assert.That(result).IsEqualTo(4_321L);
    }

    /// <summary>A call with more words than the direct form carries keeps the array path
    /// rather than failing to compile, so the arity cap costs correctness nothing.</summary>
    [Test, Timeout(10_000)]
    public async Task WideCall_StillRuns(CancellationToken ct) {
        _ = ct;
        var result = Run("""
            class P {
                P() { }
                long Sum(long a, long b, long c, long d, long e, long f, long g, long h, long i) {
                    return a + b + c + d + e + f + g + h + i;
                }
                long Run() { return this.Sum(1, 2, 3, 4, 5, 6, 7, 8, 9); }
            }
            new P().Run();
            """);

        await Assert.That(result).IsEqualTo(45L);
    }

    /// <summary>
    /// A frame whose body throws still restores the caller's frame, closure and stack
    /// pointer: the restore is a catch-and-rethrow, so the exceptional path is the only
    /// one that leaves the fast path. The call after the throw is the proof that the
    /// caller's frame survived.
    /// </summary>
    [Test, Timeout(10_000)]
    public async Task ThrowingCallee_RestoresTheCallersFrame(CancellationToken ct) {
        _ = ct;
        var typeNode = new TypeDefinitionNode(
            "Boom",
            "Sample",
            Properties: [new PropertyDefinitionNode("Last", new PrimitiveTypeReference(PrimitiveType.Int64))],
            Constructors: [new ConstructorDefinitionNode()],
            Methods: [
                new MethodDefinitionNode(
                    "Blow",
                    new PrimitiveTypeReference(PrimitiveType.Int64),
                    [new Parameter("v", new PrimitiveTypeReference(PrimitiveType.Int64))],
                    new Block([
                        new ThrowStatement(new New(new ClrTypeReference(typeof(InvalidOperationException)))),
                    ]),
                    IsStatic: false),
            ]);
        var analyzer = new TypeDefinitionNodeAnalyzer();
        var context = AnalysisContext.CreateDefault();
        analyzer.Analyze(context, typeNode);

        var boom = new Variable("boom");
        var caught = new Variable("caught");
        var node = new Block([
            new Assignment(boom, new New(new TypeReference("Sample.Boom"))),
            new Assignment(caught, new Constant(0L)),
            new TryCatchFinally(
                new Invoke(new Member(boom, "Blow"), new Constant(1L)),
                CatchClauses: [
                    new CatchClause(
                        new ClrTypeReference(typeof(InvalidOperationException)),
                        "ex",
                        new Assignment(caught, new Constant(1L)))
                ]),
            caught
        ], [boom, caught]);

        using var exec = Interpreter.Execute(Interpreter.Compile(node, context.TypeDefinitions));

        await Assert.That(exec.RawValue).IsEqualTo(1L)
            .Because("the throw reached the caller's catch, so the callee frame was unwound and "
                + "the enclosing frame stayed usable");
    }

    /// <summary>Recursion is where the allocation dominated: a frame per call, thousands
    /// of calls. The answer is the oracle.</summary>
    [Test, Timeout(10_000)]
    public async Task RecursiveMethod_StillReturnsTheSameAnswer(CancellationToken ct) {
        _ = ct;
        var result = Run("""
            class P {
                P() { }
                long Total(long n) {
                    if (n <= 0) { return 0; }
                    return n + this.Total(n - 1);
                }
                long Run() { return this.Total(20); }
            }
            new P().Run();
            """);

        await Assert.That(result).IsEqualTo(210L);
    }
}