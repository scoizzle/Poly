using System.Linq.Expressions;

using Poly.Interpretation;
using Poly.Interpretation.Vm;
using Poly.Interpretation.Vm.Abi;

namespace Poly.Tests.Interpretation;

/// <summary>
/// A small fixed-size value-type array that never escapes is promoted to frame
/// slots. This is the nqueens PolyVM hot path: four <c>new long[8]</c> buffers
/// whose every use is an element read or write. Routing those through the heap
/// costs a deref plus a cast per access (`docs/perf-comparison`). The
/// promotion must stay invisible: identical values, bounds failures, aliasing,
/// capture, and IEEE element encoding.
/// </summary>
public class SmallArrayLocalTests {
    private static AbiEmitter.BuiltVmProgram Build(Node node) =>
        AbiEmitter.Build(node, Interpreter.Analyze(node), CompilationMode.NoDebug);

    private sealed class Calls : ExpressionVisitor {
        public List<System.Reflection.MethodInfo> Methods { get; } = [];

        protected override Expression VisitMethodCall(MethodCallExpression node) {
            Methods.Add(node.Method);
            return base.VisitMethodCall(node);
        }
    }

    private static List<System.Reflection.MethodInfo> CallsIn(AbiEmitter.BuiltVmProgram built) {
        var calls = new Calls();
        calls.Visit(built.Expression);
        return calls.Methods;
    }

    private static int HeapAllocations(AbiEmitter.BuiltVmProgram built) =>
        CallsIn(built).Count(m => m.Name == nameof(VmManagedObjectHeap.Allocate)
            && m.DeclaringType == typeof(VmManagedObjectHeap));

    private static int HeapReads(AbiEmitter.BuiltVmProgram built) =>
        CallsIn(built).Count(m => m.Name == nameof(VmManagedObjectHeap.UnsafeGet)
            && m.DeclaringType == typeof(VmManagedObjectHeap));

    private sealed class Arrays : ExpressionVisitor {
        public List<NewArrayExpression> Nodes { get; } = [];

        protected override Expression VisitNewArray(NewArrayExpression node) {
            Nodes.Add(node);
            return base.VisitNewArray(node);
        }
    }

    /// <summary>Heap <c>long[]</c> buffers (<c>new long[n]</c>) the body creates.
    /// A promoted array creates none; a fallback creates one per definition.
    /// A closure cell is also a <c>long[]</c>, so the capture case allows extra.</summary>
    private static int LongArrayAllocations(AbiEmitter.BuiltVmProgram built) {
        var arrays = new Arrays();
        arrays.Visit(built.Expression);
        return arrays.Nodes.Count(n => n.Type == typeof(long[])
            && n.NodeType == ExpressionType.NewArrayBounds);
    }

    private static long Exec(Node node) {
        var program = Interpreter.Compile(node, CompilationMode.NoDebug);
        using var exec = Interpreter.Execute(program, s => s.MaxLoopIterations = 10_000);
        return (long)(exec.RawValue);
    }

    private static Node LongArray(Variable array, int length) =>
        new Assignment(array, new NewArray(TypeReference.To<long>(), new Constant(length)));

    private static Node At(Node array, Node index) => new IndexAccess(array, index);

    // ── Promotion shape ───────────────────────────────────────────

    [Test, Timeout(10_000)]
    public async Task SmallArray_ReadsAndWritesEmitNoHeapAccess(CancellationToken ct) {
        var a = new Variable("a");
        var built = Build(new Block([
            LongArray(a, 8),
            new Assignment(At(a, new Constant(3L)), new Constant(7L)),
            At(a, new Constant(3L))
        ], [a]));

        await Assert.That(HeapAllocations(built)).IsEqualTo(0);
        await Assert.That(HeapReads(built)).IsEqualTo(0);
    }

    [Test, Timeout(10_000)]
    public async Task SmallArray_ValuesRoundTripAcrossAWholeBuffer(CancellationToken ct) {
        var a = new Variable("a");
        var i = new Variable("i");
        var total = new Variable("total");
        var body = new Block([
            LongArray(a, 8),
            new Assignment(i, new Constant(0L)),
            new Assignment(total, new Constant(0L)),
            new WhileLoop(new LessThan(i, new Constant(8L)), new Block([
                new Assignment(At(a, i), new Add(i, new Constant(1L))),
                new Assignment(total, new Add(total, At(a, i))),
                new Assignment(i, new Add(i, new Constant(1L)))
            ])),
            total
        ], [a, i, total]);

        await Assert.That(Exec(body)).IsEqualTo(36L);
        await Assert.That(HeapAllocations(Build(body))).IsEqualTo(0);
    }

    [Test, Timeout(10_000)]
    public async Task SmallArray_IeeeElementRoundTrips(CancellationToken ct) {
        var a = new Variable("a");
        var body = new Block([
            new Assignment(a, new NewArray(TypeReference.To<double>(), new Constant(4))),
            new Assignment(At(a, new Constant(1L)), new Constant(1.5)),
            new TypeCast(At(a, new Constant(1L)), TypeReference.To<long>())
        ], [a]);

        await Assert.That(Exec(body)).IsEqualTo(1L);
    }

    // ── Fallback: the array escapes ───────────────────────────────

    [Test, Timeout(10_000)]
    public async Task AliasedSmallArray_StaysOnTheHeapAndReadsThroughTheAlias(CancellationToken ct) {
        var a = new Variable("a");
        var b = new Variable("b");
        var body = new Block([
            LongArray(a, 4),
            new Assignment(At(a, new Constant(0L)), new Constant(5L)),
            new Assignment(b, a),
            At(b, new Constant(0L))
        ], [a, b]);

        await Assert.That(Exec(body)).IsEqualTo(5L);
        await Assert.That(LongArrayAllocations(Build(body))).IsEqualTo(1);
    }

    [Test, Timeout(10_000)]
    public async Task ReassignedSmallArray_StaysOnTheHeap(CancellationToken ct) {
        var a = new Variable("a");
        var body = new Block([
            LongArray(a, 4),
            LongArray(a, 4),
            new Assignment(At(a, new Constant(2L)), new Constant(9L)),
            At(a, new Constant(2L))
        ], [a]);

        await Assert.That(Exec(body)).IsEqualTo(9L);
        await Assert.That(LongArrayAllocations(Build(body))).IsEqualTo(2);
    }

    [Test, Timeout(10_000)]
    public async Task ArrayLengthOfASmallArray_IsTheDeclaredLength(CancellationToken ct) {
        var a = new Variable("a");
        var body = new Block([
            LongArray(a, 3),
            new Member(a, "Length")
        ], [a]);

        await Assert.That(Exec(body)).IsEqualTo(3L);
    }

    [Test, Timeout(10_000)]
    public async Task CapturedSmallArray_StaysOnTheHeap(CancellationToken ct) {
        var a = new Variable("a");
        var fn = new Variable("fn");
        var body = new Block([
            LongArray(a, 4),
            new Assignment(At(a, new Constant(2L)), new Constant(6L)),
            new Assignment(fn, new Lambda([], At(a, new Constant(2L)))),
            new Invoke(fn)
        ], [a, fn]);

        await Assert.That(Exec(body)).IsEqualTo(6L);
        await Assert.That(LongArrayAllocations(Build(body))).IsGreaterThanOrEqualTo(1);
    }

    [Test, Timeout(10_000)]
    public async Task UseBeforeDefinition_StaysOnTheHeap(CancellationToken ct) {
        var a = new Variable("a");
        // A read that textually precedes the definition cannot be served by a
        // frame window that is only initialized at the definition.
        var body = new Block([
            At(a, new Constant(0L)),
            LongArray(a, 4)
        ], [a]);

        await Assert.That(LongArrayAllocations(Build(body))).IsEqualTo(1);
    }

    // ── Boundary semantics ────────────────────────────────────────

    [Test, Timeout(10_000)]
    public async Task SmallArray_OutOfRangeWriteThrows(CancellationToken ct) {
        var a = new Variable("a");
        var body = new Block([
            LongArray(a, 3),
            new Assignment(At(a, new Constant(3L)), new Constant(1L)),
            new Constant(0L)
        ], [a]);

        await Assert.That(() => Exec(body)).Throws<IndexOutOfRangeException>();
    }

    [Test, Timeout(10_000)]
    public async Task SmallArray_OutOfRangeReadThrows(CancellationToken ct) {
        var a = new Variable("a");
        var body = new Block([
            LongArray(a, 3),
            At(a, new Constant(3L))
        ], [a]);

        await Assert.That(() => Exec(body)).Throws<IndexOutOfRangeException>();
    }
}