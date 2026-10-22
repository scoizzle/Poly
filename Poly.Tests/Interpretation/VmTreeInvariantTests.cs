using System.Linq.Expressions;

using Poly.Ast.Nodes;
using Poly.Interpretation;
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
}