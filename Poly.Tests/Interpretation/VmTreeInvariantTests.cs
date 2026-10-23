using System.Linq.Expressions;

using Poly.Ast.Nodes;
using Poly.Interpretation;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.Vm;
using Poly.Interpretation.Vm.Abi;
using Poly.Tests.TestHelpers;

using static System.Linq.Expressions.Expression;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Runs <see cref="VmTree.AssertInvariants"/> over a corpus of emitted bodies, so the
/// tree-wide conventions are checked in one place instead of per emitter: ring locals
/// are the prefix <c>0..MaxRingDepth-1</c>, no declared local is unused, and no frame
/// slot is addressed absolutely.
/// </summary>
public class VmTreeInvariantTests {
    private static void AssertClean(Node node) {
        VmTree.AssertInvariants(AbiEmitter.Build(node, Interpreter.Analyze(node), CompilationMode.NoDebug));
        VmTree.AssertInvariants(AbiEmitter.Build(node, Interpreter.Analyze(node), CompilationMode.Normal));
    }

    [Test, Timeout(10_000)]
    public async Task Block_WithLocals_IsClean(CancellationToken ct) {
        var a = new Variable("a");
        var b = new Variable("b");
        AssertClean(new Block([
            new Assignment(a, new Constant(1L)),
            new Assignment(b, new Add(a, new Constant(2L))),
        ], [a, b]));
    }

    [Test, Timeout(10_000)]
    public async Task IfElse_IsClean(CancellationToken ct) {
        var a = new Variable("a");
        AssertClean(new Block([
            new IfStatement(new Constant(true),
                new Assignment(a, new Constant(1L)),
                new Assignment(a, new Constant(2L))),
        ], [a]));
    }

    [Test, Timeout(10_000)]
    public async Task WhileLoop_IsClean(CancellationToken ct) {
        var a = new Variable("a");
        AssertClean(new Block([
            new WhileLoop(new Constant(false), new Assignment(a, new Constant(1L))),
        ], [a]));
    }

    /// <summary>The SetArgs inline path: a lambda with a parameter and an invoke with
    /// no argument expressions.</summary>
    [Test, Timeout(10_000)]
    public async Task SetArgsInvoke_IsClean(CancellationToken ct) {
        var p = new Parameter("p", TypeReference.To<long>());
        AssertClean(new Add(new Constant(41L), new Invoke(new Lambda([p], p))));
    }

    /// <summary>The checker must not be vacuous: a dead local has to be reported, or
    /// this file proves nothing about the corpus above.</summary>
    [Test, Timeout(10_000)]
    public async Task Checker_ReportsADeadLocal(CancellationToken ct) {
        var dead = Variable(typeof(long), "_dead");
        var live = Variable(typeof(long), "_live");
        // Explicit array: the params-Expression[] overload would make these locals body
        // expressions instead of declarations, and the checker would see nothing.
        var scan = VmTree.Read(Block(new ParameterExpression[] { dead, live },
            Assign(live, Constant(1L))));

        await Assert.That(scan.Declared.Count).IsEqualTo(2);
        await Assert.That(scan.DeadLocals.Count(d => d.Name == "_dead")).IsEqualTo(1);
    }

    /// <summary>And a constant <c>_slots</c> index has to be reported, since that is
    /// the frame-discipline violation the check exists for.</summary>
    [Test, Timeout(10_000)]
    public async Task Checker_ReportsAnAbsoluteSlotAccess(CancellationToken ct) {
        var slots = Variable(typeof(long[]), "_slots");
        var scan = VmTree.Read(Block(new ParameterExpression[] { slots }, ArrayIndex(slots, Constant(3))));

        await Assert.That(scan.AbsoluteSlotAccesses.Count).IsEqualTo(1);
    }

    /// <summary>Every <c>CompileNodeInner</c> arm leaves its result at <c>RingDepth - 1</c>.
    /// A statement arm leaves <c>0L</c> there, so a consumer reads the result slot without
    /// first asking what kind of node produced it.
    /// <para>The check is structural — it reads the emitted tree, not the emit-time cursor —
    /// because a slot the emitter reserves and never writes reads whatever the previous
    /// statement left there, and the cursor cannot tell the two apart. It runs over the
    /// parity corpus (real programs) and over one program per statement arm, whose root is
    /// that arm. An arm that forgets shows up as a program with no ring result, and a
    /// reserve-without-write shows up as a slot nothing assigns.</para>
    /// </summary>
    [Test, Timeout(120_000)]
    public async Task EveryCompileNodeArm_LeavesItsResultInTheRing(CancellationToken ct) {
        var problems = new List<string>();
        foreach (var (label, program) in ArmCases) {
            var analysis = Interpreter.Analyze(program);
            // A folded or rewritten case would check the replacement's arm, not this one.
            if (analysis.GetNodeReplacement(program) is { } replaced && !ReferenceEquals(replaced, program)) {
                problems.Add(
                    $"{label}: analysis replaced the node with {replaced.GetType().Name}, "
                    + "so the case does not exercise its arm");
                continue;
            }
            if (TryBuild(program, analysis) is { } built)
                problems.AddRange(CheckRingResult(label, built));
        }
        foreach (var (label, program) in AstParityTests.Corpus()
            .Select(corpusCase => (corpusCase.Label, corpusCase.Program))) {
            if (TryBuild(program, Interpreter.Analyze(program)) is { } built)
                problems.AddRange(CheckRingResult(label, built));
        }

        await Assert.That(problems).IsEmpty()
            .Because(string.Join(Environment.NewLine, problems));
    }

    /// <summary>The emitted program, or null when compiling it is a recorded rejection:
    /// there is no body to scan.</summary>
    private static AbiEmitter.BuiltVmProgram? TryBuild(Node program, AnalysisResult analysis) {
        try {
            return AbiEmitter.Build(program, analysis, CompilationMode.NoDebug);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException) {
            return null;
        }
    }

    /// <summary>One program per statement arm, each the root of its own program: a statement
    /// that was never the root of anything would otherwise go unchecked.</summary>
    private static IEnumerable<(string Label, Node Program)> ArmCases {
        get {
            var item = new Variable("item");
            yield return ("IfStatement", new IfStatement(
                new New(TypeReference.To<AstParityTests.ParityWidget>()), new Constant(2L)));
            yield return ("WhileLoop", new WhileLoop(new Constant(false), new Constant(1L)));
            yield return ("DoWhileLoop", new DoWhileLoop(new Constant(1L), new Constant(false)));
            yield return ("ForLoop", new ForLoop(null, new Constant(false), null, new Constant(1L)));
            yield return ("ForEachLoop",
                new ForEachLoop(item, new Constant(new[] { 1, 2 }), new Assignment(item, item)));
            yield return ("Return_WithValue", new Return(new Constant(7L)));
            yield return ("Return_Void", new Return());
            yield return ("BreakStatement",
                new WhileLoop(new Constant(true), new Block([new BreakStatement()])));
            yield return ("ContinueStatement",
                new WhileLoop(new Constant(false), new Block([new ContinueStatement()])));
            yield return ("GotoStatement",
                new Block([new LabelDeclaration("end", new Constant(1L)), new GotoStatement("end")]));
            yield return ("LabelDeclaration", new LabelDeclaration("start", new Constant(1L)));
            yield return ("ThrowStatement",
                new ThrowStatement(new New(TypeReference.To<InvalidOperationException>())));
            yield return ("TryCatchFinally",
                new TryCatchFinally(new Constant(1L), null, new Constant(2L)));
            yield return ("UsingStatement",
                new UsingStatement(new New(TypeReference.To<MemoryStream>()), new Constant(1L)));
            yield return ("SuspendNode", new SuspendNode(new Constant(1L)));
        }
    }

    /// <summary>What the arm left behind in the emitted body: a result slot, a write to it,
    /// and a write to every other slot the body declares.</summary>
    private static IEnumerable<string> CheckRingResult(string label, AbiEmitter.BuiltVmProgram built) {
        var scan = VmTree.Read(built.Expression);
        var written = scan.AssignedRingIndexes.ToHashSet();
        if (built.RootResult is not SlotLocation.Ring result) {
            yield return $"{label}: the arm left no ring result";
            yield break;
        }
        if (!written.Contains(result.Slot))
            yield return $"{label}: _r{result.Slot} is the result slot and nothing writes it";
        foreach (var slot in scan.RingIndexes.Where(index => !written.Contains(index)))
            yield return $"{label}: _r{slot} is declared but never written";
    }
}