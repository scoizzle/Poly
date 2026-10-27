using System.Linq.Expressions;

using Poly.Interpretation;
using Poly.Interpretation.Vm;
using Poly.Interpretation.Vm.Abi;
using Poly.Tests.TestHelpers;

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

    /// <summary>Variable leases. A variable is a pool lease in the ring family, so
    /// the count is not every <c>_rN</c>: statement temps share those names. The
    /// 33rd simultaneous variable has no lease and stays in its frame slot.</summary>
    private static int RegisterLocalCount(AbiEmitter.BuiltVmProgram built) =>
        VmTree.Read(built.Expression).Registers.Count;

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
        await Assert.That(RegisterLocalCount(built)).IsEqualTo(0)
            .Because("a constant has a ring temp for its result, and that temp is not a variable lease");
    }

    [Test]
    public async Task OneVariable_EmitsOnlyItsRegister() {
        var built = Build(CompilationMode.Normal);
        var leases = VmTree.Read(built.Expression).Registers;
        await Assert.That(leases.Count).IsEqualTo(1)
            .Because("one long variable is one pool lease; _reg0 is gone");
        await Assert.That(leases.Single().Name).IsEqualTo("_r0")
            .Because("the long lease is the pool's _r0, shared with temps at later indexes");
    }

    [Test]
    public async Task SiblingScopes_ReuseOneRegisterLocal() {
        var a = new Variable("a");
        var b = new Variable("b");
        var built = Build(new Block([
            new Block([new Assignment(a, new Constant(1L)), a], [a]),
            new Block([new Assignment(b, new Constant(2L)), b], [b])
        ], []));
        await Assert.That(RegisterLocalCount(built)).IsEqualTo(1)
            .Because("the second block reuses the released _r0 lease, so two variables are one local");
    }

    [Test]
    public async Task NineSimultaneousVariables_GrowByOne() {
        var vars = Enumerable.Range(0, 9).Select(i => new Variable($"v{i}")).ToArray();
        var nodes = vars.Select((v, i) => (Node)new Assignment(v, new Constant((long)i))).ToList();
        var built = Build(new Block(nodes, vars));
        await Assert.That(RegisterLocalCount(built)).IsEqualTo(9)
            .Because("nine simultaneous variables are _r0.._r8; the value temp _r9 is not a lease");
    }

    [Test]
    public async Task ThirtyThreeSimultaneousVariables_FallBackToFrameSlots() {
        var vars = Enumerable.Range(0, 33).Select(i => new Variable($"v{i}")).ToArray();
        var nodes = vars.Select((v, i) => (Node)new Assignment(v, new Constant((long)i))).ToList();
        nodes.Add(vars[32]);
        var built = Build(new Block(nodes, vars));

        await Assert.That(RegisterLocalCount(built)).IsEqualTo(32)
            .Because("thirty-two pool leases; the 33rd variable has no lease and lives at frame slot 32");
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
    public async Task NoDebug_PublishesTheResultAndDoesNotCopyTheLease() {
        var built = Build(CompilationMode.NoDebug);
        var all = new Collector();
        all.Visit(built.Expression);
        await Assert.That(all.IndexAssignments.Count).IsEqualTo(1)
            .Because("NoDebug writes the result word and does not copy the lease into _slots");
    }

    [Test]
    public async Task Normal_LeaseCopiesSitInsideTheDebugHookGuard() {
        var built = Build(CompilationMode.Normal);
        await Assert.That(SlotAssignsOutsideHook(built.Expression)).IsEqualTo(1)
            .Because("the result word is the only _slots store outside if (DebugHook != null)");
    }

    [Test]
    public async Task Normal_CatchExitCopySitsInsideTheDebugHookGuard() {
        var x = new Variable("x");
        var node = new Block([
            new TryCatchFinally(
                new Block([new Assignment(x, new Constant(1L))]),
                [new CatchClause(TypeReference.To<Exception>(), "ex", new Assignment(x, new Constant(2L)))]),
            x
        ], [x]);
        var built = Build(node, CompilationMode.Normal);
        await Assert.That(SlotAssignsOutsideHook(built.Expression)).IsEqualTo(1)
            .Because("a catch-exit lease copy is an observation store and sits inside the hook guard");
    }

    private static int SlotAssignsOutsideHook(Expression expression) {
        var walker = new OutsideHookSlots();
        walker.Visit(expression);
        return walker.Count;
    }

    private sealed class OutsideHookSlots : ExpressionVisitor {
        public int Count { get; private set; }
        private int _hookDepth;

        public override Expression? Visit(Expression? node) {
            if (node is ConditionalExpression conditional && IsHookGuard(conditional)) {
                Visit(conditional.Test);
                _hookDepth++;
                Visit(conditional.IfTrue);
                _hookDepth--;
                Visit(conditional.IfFalse);
                return node;
            }
            if (_hookDepth == 0
                && node is BinaryExpression {
                    NodeType: ExpressionType.Assign,
                    Left: IndexExpression { Object: ParameterExpression { Name: "_slots" } }
                })
                Count++;
            return base.Visit(node);
        }

        private static bool IsHookGuard(ConditionalExpression conditional) =>
            conditional.Test is BinaryExpression {
                NodeType: ExpressionType.NotEqual,
                Left: MemberExpression { Member.Name: "DebugHook" }
            };
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