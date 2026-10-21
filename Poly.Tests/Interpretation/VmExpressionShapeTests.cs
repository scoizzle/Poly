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

    private static AbiEmitter.BuiltVmProgram Build(CompilationMode mode) {
        var node = Sample();
        return AbiEmitter.Build(node, Interpreter.Analyze(node), mode);
    }

    private sealed class Collector : ExpressionVisitor {
        public List<MemberExpression> Members { get; } = [];
        public List<ConditionalExpression> Conditionals { get; } = [];
        public List<IndexExpression> IndexAssignments { get; } = [];

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