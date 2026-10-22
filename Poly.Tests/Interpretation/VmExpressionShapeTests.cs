using System.Linq.Expressions;

using Poly.Interpretation;
using Poly.Interpretation.Vm;
using Poly.Interpretation.Vm.Abi;

namespace Poly.Tests.Interpretation;

/// <summary>Locks the emitted body's debug/no-debug shape; the VM README renders
/// the same trees.</summary>
public class VmExpressionShapeTests {
    private static Node Sample() {
        var x = new Variable("x");
        return new Block([
            new Assignment(x, new Constant(2L)),
            new Add(x, new Constant(1L))
        ], [x]);
    }

    private static AbiEmitter.BuiltVmProgram Build(CompilationMode mode) =>
        Build(Sample(), mode);

    private static AbiEmitter.BuiltVmProgram Build(Node node, CompilationMode mode = CompilationMode.Normal) =>
        AbiEmitter.Build(node, Interpreter.Analyze(node), mode);

    /// <summary>The register locals the compiled delegate carries. The count is the
    /// high-water of simultaneous register-backed variables; a variable beyond
    /// <c>MaxRegisterCount</c> stays in its frame slot instead.</summary>
    private static int RegisterLocalCount(AbiEmitter.BuiltVmProgram built) =>
        ((BlockExpression)built.Expression.Body).Variables
            .Count(v => v.Name?.StartsWith("_reg", StringComparison.Ordinal) == true);

    private sealed class Collector : ExpressionVisitor {
        public List<MemberExpression> Members { get; } = [];
        public List<ConditionalExpression> Conditionals { get; } = [];
        public List<IndexExpression> IndexAssignments { get; } = [];
        public List<IndexExpression> Indexes { get; } = [];

        protected override Expression VisitMember(MemberExpression node) {
            Members.Add(node);
            return base.VisitMember(node);
        }

        protected override Expression VisitConditional(ConditionalExpression node) {
            Conditionals.Add(node);
            return base.VisitConditional(node);
        }

        protected override Expression VisitBinary(BinaryExpression node) {
            if (node.NodeType is ExpressionType.Assign && node.Left is IndexExpression index)
                IndexAssignments.Add(index);
            return base.VisitBinary(node);
        }

        protected override Expression VisitIndex(IndexExpression node) {
            Indexes.Add(node);
            return base.VisitIndex(node);
        }
    }

    // ── Register-file allocation shape ─────────────────────────────

    [Test]
    public async Task NoVariables_EmitsNoRegisterLocals() {
        var built = Build(new Constant(1L));
        await Assert.That(RegisterLocalCount(built)).IsEqualTo(0);
    }

    [Test]
    public async Task OneVariable_EmitsOnlyItsRegister() {
        var built = Build(CompilationMode.Normal);
        await Assert.That(RegisterLocalCount(built)).IsEqualTo(1);
        await Assert.That(((BlockExpression)built.Expression.Body).Variables
            .Count(v => v.Name == "_reg0")).IsEqualTo(1);
    }

    [Test]
    public async Task SiblingScopes_ReuseOneRegisterLocal() {
        var a = new Variable("a");
        var b = new Variable("b");
        var built = Build(new Block([
            new Block([new Assignment(a, new Constant(1L)), a], [a]),
            new Block([new Assignment(b, new Constant(2L)), b], [b])
        ], []));
        await Assert.That(RegisterLocalCount(built)).IsEqualTo(1);
    }

    [Test]
    public async Task NineSimultaneousVariables_GrowByOne() {
        var vars = Enumerable.Range(0, 9).Select(i => new Variable($"v{i}")).ToArray();
        var nodes = vars.Select((v, i) => (Node)new Assignment(v, new Constant((long)i))).ToList();
        var built = Build(new Block(nodes, vars));
        await Assert.That(RegisterLocalCount(built)).IsEqualTo(9);
    }

    [Test]
    public async Task ThirtyThreeSimultaneousVariables_FallBackToFrameSlots() {
        var vars = Enumerable.Range(0, 33).Select(i => new Variable($"v{i}")).ToArray();
        var nodes = vars.Select((v, i) => (Node)new Assignment(v, new Constant((long)i))).ToList();
        nodes.Add(vars[32]);
        var built = Build(new Block(nodes, vars));

        await Assert.That(RegisterLocalCount(built)).IsEqualTo(32);
        await Assert.That(built.DebugInfo.Variables.Count).IsEqualTo(33);

        var all = new Collector();
        all.Visit(built.Expression);
        await Assert.That(all.Indexes.Any(i =>
            i.Object is ParameterExpression { Name: "_slots" }
            && i.Arguments is [BinaryExpression {
                NodeType: ExpressionType.Add,
                Right: ConstantExpression { Value: 32 }
            }])).IsTrue();
    }

    [Test]
    public async Task Normal_ScopeStoreSitsInsideTheDebugHookGuard() {
        var built = Build(CompilationMode.Normal);
        var all = new Collector();
        all.Visit(built.Expression);

        var guards = all.Conditionals.Where(c =>
            c.Test is BinaryExpression {
                NodeType: ExpressionType.NotEqual,
                Left: MemberExpression { Member.Name: "DebugHook" }
            }).ToList();
        await Assert.That(guards.Count).IsGreaterThan(0);

        // The root statement's scope has no locals; a per-statement guard does.
        var stores = 0;
        foreach (var guard in guards) {
            var inside = new Collector();
            inside.Visit(guard.IfTrue);
            stores += inside.IndexAssignments.Count;
        }
        await Assert.That(stores).IsGreaterThan(0);
    }

    [Test]
    public async Task NoDebug_HasNoDebugHook() {
        var built = Build(CompilationMode.NoDebug);
        var all = new Collector();
        all.Visit(built.Expression);

        await Assert.That(all.Members.Any(m => m.Member.Name == "DebugHook")).IsFalse();
        await Assert.That(all.Conditionals.Any(c =>
            c.Test is BinaryExpression {
                NodeType: ExpressionType.NotEqual,
                Left: MemberExpression { Member.Name: "DebugHook" }
            })).IsFalse();
    }

    [Test]
    public async Task NeitherMode_DeclaresTheDeadProgramCounterLocal() {
        foreach (var mode in new[] { CompilationMode.Normal, CompilationMode.NoDebug }) {
            var built = Build(mode);
            var block = (BlockExpression)built.Expression.Body;
            await Assert.That(block.Variables.Any(v => v.Name == "_pc")).IsFalse();
        }
    }
}