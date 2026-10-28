using System.Linq.Expressions;
using System.Text.RegularExpressions;

using Poly.Ast.Nodes;
using Poly.Interpretation;
using Poly.Interpretation.Vm;
using Poly.Interpretation.Vm.Abi;

namespace Poly.Tests.Interpretation;

/// <summary>
/// IS7e: a heap handle is released by one explicit call, <c>Recycle</c>, and by nothing
/// else. A null store used to be a free; it no longer is, so a store can never hand a
/// handle to a later allocation while another holder still has it.
/// </summary>
public class HeapLifetimeTests {
    /// <summary>A null store is a value, not a free: the slot keeps the handle and reads
    /// back null, so a later allocation cannot take a handle that is still in use.</summary>
    [Test]
    public async Task SetNull_DoesNotRecycle() {
        var heap = new VmManagedObjectHeap();
        int handle = heap.Allocate("a");

        heap.Set(handle, null);

        await Assert.That(heap.Get(handle)).IsNull()
            .Because("the handle stays live and now holds null");
        await Assert.That(heap.UnsafeGet(handle)).IsNull();
        await Assert.That(heap.Allocate("b")).IsNotEqualTo(handle)
            .Because("a null store must not put the handle on the free-list");
        await Assert.That(heap.Get(handle)).IsNull()
            .Because("nothing overwrote the slot a second allocation would have taken");
        await Assert.That(heap.Count).IsEqualTo(3);
    }

    [Test]
    public async Task UnsafeSetNull_DoesNotRecycle() {
        var heap = new VmManagedObjectHeap();
        int handle = heap.Allocate("a");

        heap.UnsafeSet(handle, null);

        await Assert.That(heap.UnsafeGet(handle)).IsNull();
        await Assert.That(heap.Allocate("b")).IsNotEqualTo(handle)
            .Because("the unsafe store is the same contract as the safe one");
    }

    [Test]
    public async Task Recycle_ThenAllocate_ReusesTheHandle() {
        var heap = new VmManagedObjectHeap();
        int first = heap.Allocate("a");
        int second = heap.Allocate("b");

        heap.Recycle(first);

        await Assert.That(heap.Get(first)).IsNull()
            .Because("a recycled handle's slot is cleared");
        int reused = heap.Allocate("c");
        await Assert.That(reused).IsEqualTo(first)
            .Because("the freed handle is the first one allocation hands out");
        await Assert.That(heap.Get(reused)).IsEqualTo("c");
        await Assert.That(heap.Count).IsEqualTo(3)
            .Because("reuse does not grow the heap past what it already issued: "
                + $"second={second}, count={heap.Count}");
    }

    [Test]
    public async Task RecycleTwice_FailsClosed() {
        var heap = new VmManagedObjectHeap();
        int handle = heap.Allocate("a");
        heap.Recycle(handle);

        await Assert.That(() => heap.Recycle(handle))
            .Throws<InvalidOperationException>()
            .Because("a second recycle is a double free: it would put one handle on the "
                + "free-list twice and hand the same slot to two allocations");
    }

    [Test]
    public async Task Recycle_HandleZero_Throws() {
        var heap = new VmManagedObjectHeap();
        int outOfRange = heap.Allocate("a");

        await Assert.That(() => heap.Recycle(0))
            .Throws<ArgumentOutOfRangeException>()
            .Because("handle 0 is the ABI null sentinel and is never a live object");
        await Assert.That(() => heap.Recycle(outOfRange + 1))
            .Throws<ArgumentOutOfRangeException>()
            .Because("a handle the heap never issued is not recyclable");
    }

    /// <summary>A freed handle stays freed until allocation hands it out again, so a store
    /// into it fails closed instead of resurrecting the slot behind the free-list's back.</summary>
    [Test]
    public async Task Set_OnRecycledHandle_FailsClosed() {
        var heap = new VmManagedObjectHeap();
        int handle = heap.Allocate("a");
        heap.Recycle(handle);

        await Assert.That(() => heap.Set(handle, "b"))
            .Throws<InvalidOperationException>();
        await Assert.That(() => heap.UnsafeSet(handle, "b"))
            .Throws<InvalidOperationException>();
    }

    private static readonly Regex ArrayCacheLocal = new("^_f?arr\\d+$", RegexOptions.Compiled);
    // A variable is a pool lease (_r/_d/_f/_i/_n), not a _reg local.
    private static readonly Regex RegisterLocal = new("^_(?:[rdfi]\\d+|n\\d+_\\w+)$", RegexOptions.Compiled);

    private static AbiEmitter.BuiltVmProgram Build(Node program) =>
        AbiEmitter.Build(program, Interpreter.Analyze(program), CompilationMode.NoDebug);

    private static Block ArrayProgram(Node newArray, Variable array, params Node[] rest) {
        var statements = new List<Node> { new Assignment(array, newArray) };
        statements.AddRange(rest);
        return new Block(statements, [array]);
    }

    /// <summary>The promote-able shape every structural row uses: one <c>new long[4]</c>
    /// whose only other references are element accesses.</summary>
    private static Block PromotableProgram(Variable cols) => ArrayProgram(
        new NewArray(TypeReference.To<long>(), new Constant(4L)), cols,
        new Assignment(new IndexAccess(cols, new Constant(0L)), new Constant(7L)),
        new IndexAccess(cols, new Constant(0L)));

    private sealed class HeapCalls : ExpressionVisitor {
        public List<MethodCallExpression> Allocations { get; } = [];
        public List<MethodCallExpression> Recycles { get; } = [];
        public List<BlockExpression> Blocks { get; } = [];

        protected override Expression VisitBlock(BlockExpression node) {
            Blocks.Add(node);
            return base.VisitBlock(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node) {
            if (node.Method.DeclaringType == typeof(VmManagedObjectHeap)) {
                if (node.Method.Name == nameof(VmManagedObjectHeap.Recycle))
                    Recycles.Add(node);
                else if (node.Method.Name == nameof(VmManagedObjectHeap.Allocate))
                    Allocations.Add(node);
            }
            return base.VisitMethodCall(node);
        }
    }

    /// <summary>The handle argument must read the variable's own lease or its frame
    /// slot, not a constant the emitter invented.</summary>
    private sealed class SlotReadFinder : ExpressionVisitor {
        public bool Found { get; private set; }

        protected override Expression VisitParameter(ParameterExpression node) {
            if (RegisterLocal.IsMatch(node.Name ?? string.Empty))
                Found = true;
            return base.VisitParameter(node);
        }

        protected override Expression VisitBinary(BinaryExpression node) {
            if (node.NodeType == ExpressionType.ArrayIndex
                && node.Left is ParameterExpression { Name: "_slots" })
                Found = true;
            return base.VisitBinary(node);
        }
    }

    private static bool IsCacheClear(Expression expression) =>
        expression is BinaryExpression {
            NodeType: ExpressionType.Assign,
            Left: ParameterExpression local,
            Right: ConstantExpression { Value: null }
        } && ArrayCacheLocal.IsMatch(local.Name ?? string.Empty);

    private static async Task AssertNoRecycle(Node program, string escape) {
        var scan = new HeapCalls();
        scan.Visit(Build(program).Expression);
        var recycles = scan.Recycles.Select(call => call.ToString()).ToList();
        await Assert.That(recycles).IsEmpty()
            .Because($"an array that escapes ({escape}) is never proven dead, so no recycle "
                + "may be emitted for its handle: " + string.Join(", ", recycles));
    }

    private static (long Value, int Count, object?[] Slots) RunWithHeap(Node program) {
        using var exec = Interpreter.Execute(Interpreter.Compile(program, CompilationMode.NoDebug));
        var heap = exec.State.Heap;
        int count = heap.Count;
        var slots = new object?[count];
        for (int handle = 1; handle < count; handle++)
            slots[handle] = heap.Get(handle);
        return (exec.RawValue, count, slots);
    }

    /// <summary>The structural half of the memory win: the handle the definition put in the
    /// variable's slot is released at the block exit, beside IS7d's <c>_arrN = null</c>.
    /// One array, one allocation, one recycle — and the recycle reads the slot.</summary>
    [Test, Timeout(10_000)]
    public async Task PromotedArray_ScopeExit_RecyclesTheHandle(CancellationToken ct) {
        var cols = new Variable("cols");
        var scan = new HeapCalls();
        scan.Visit(Build(PromotableProgram(cols)).Expression);

        await Assert.That(scan.Allocations.Count).IsEqualTo(1)
            .Because("a promoted definition is one allocation");
        await Assert.That(scan.Recycles.Count).IsEqualTo(1)
            .Because("one promoted array released at one exit is one recycle: "
                + string.Join(", ", scan.Recycles.Select(call => call.ToString())));

        var recycle = scan.Recycles[0];
        var exit = scan.Blocks.FirstOrDefault(
            block => block.Expressions.Contains(recycle));
        await Assert.That(exit).IsNotNull()
            .Because("the recycle is a statement of the block it ends, not a nested temp");

        int index = exit!.Expressions.IndexOf(recycle);
        await Assert.That(index).IsGreaterThanOrEqualTo(exit.Expressions.Count - 3)
            .Because("the recycle sits at the block exit, after every statement: index "
                + $"{index} of {exit.Expressions.Count}");
        var beside = exit.Expressions[index + 1];
        await Assert.That(IsCacheClear(beside)).IsTrue()
            .Because("it is emitted beside IS7d's `_arrN = null`, and the clear is what "
                + $"comes next in tree order: {beside}");
        var afterExit = exit.Expressions.Skip(index + 2).ToList();
        await Assert.That(afterExit.Count).IsLessThanOrEqualTo(1)
            .Because("nothing is left at the exit but the block's own value read: "
                + string.Join(", ", afterExit.Select(item => item.ToString())));

        var argument = recycle.Arguments.Single();
        await Assert.That(argument.Type).IsEqualTo(typeof(int))
            .Because("Recycle takes the handle as an int");
        var slotRead = new SlotReadFinder();
        slotRead.Visit(argument);
        await Assert.That(slotRead.Found).IsTrue()
            .Because("the handle is read from the variable's lease before the cache is "
                + $"cleared: {argument}");
    }

    /// <summary>The memory win: the same array is allocated every iteration and released
    /// every iteration, so the heap's high-water mark is the working set, not the
    /// iteration count.</summary>
    [Test, Timeout(120_000)]
    public async Task ArrayInLoopBody_HeapStaysBounded(CancellationToken ct) {
        var (shortCount, shortIterations) = RunLoop(10);
        var (longCount, longIterations) = RunLoop(10_000);

        await Assert.That(shortIterations).IsEqualTo(10L)
            .Because("the loop must actually run, or a bounded heap proves nothing");
        await Assert.That(longIterations).IsEqualTo(10_000L);
        await Assert.That(longCount).IsLessThanOrEqualTo(4)
            .Because($"10k iterations of a constant working set kept {longCount} slots");
        await Assert.That(longCount).IsEqualTo(shortCount)
            .Because($"the high-water mark tracks the working set, not the iteration count: "
                + $"10 iterations kept {shortCount} slots, 10k kept {longCount}");
    }

    /// <summary>A block's value is its last statement's value, so a definition that ends
    /// the block hands the handle to the enclosing expression. The predicate cannot see
    /// that, so the release is withheld: the handle is still observed.</summary>
    [Test, Timeout(10_000)]
    public async Task BlockValueArray_IsNotRecycled(CancellationToken ct) {
        var cols = new Variable("cols");
        var program = new Block([
            new Assignment(cols, new NewArray(TypeReference.To<long>(), new Constant(4L))),
        ], [cols]);

        await AssertNoRecycle(program, "the block's own value");
        var (handle, _, slots) = RunWithHeap(program);
        await Assert.That(handle).IsGreaterThan(0L)
            .Because("the block's value is the array handle the definition produced");
        await Assert.That(slots[(int)handle]).IsAssignableTo<long[]>()
            .Because("a caller that received the block's value can still read the array");
    }

    /// <summary>A <c>StridedSetBits</c> receiver indexes the buffer exactly as an
    /// <c>IndexAccess</c> does — it is never handed on — so it counts as an element access and
    /// the array is promoted and released like any other. <para><b>Contract change (IS7h).</b>
    /// This test previously asserted the opposite: that <c>StridedSetBits</c> was "an argument,
    /// not an element access", so no promotion and no recycle. That rule cost the sieve's mark
    /// loop two heap derefs per iteration. Promotion is safe here for the same reason it is safe
    /// for an index access: the handle never leaves the statement.</para></summary>
    [Test, Timeout(10_000)]
    public async Task StridedSetBits_IsAReceiver_SoTheArrayIsPromotedAndRecycled(CancellationToken ct) {
        var cols = new Variable("cols");
        var program = ArrayProgram(
            new NewArray(TypeReference.To<long>(), new Constant(4L)), cols,
            new StridedSetBits(cols, new Constant(0L), new Constant(1L), new Constant(3L)),
            new IndexAccess(cols, new Constant(0L)));

        var scan = new HeapCalls();
        scan.Visit(Build(program).Expression);
        await Assert.That(scan.Allocations.Count).IsEqualTo(1)
            .Because("a promoted definition is one allocation");
        await Assert.That(scan.Recycles.Count).IsEqualTo(1)
            .Because("the receiver is an element access, so the handle is proven dead at the "
                + "block exit: " + string.Join(", ", scan.Recycles.Select(call => call.ToString())));

        // Bits 0..3 are set, so word 0 is 0b1111.
        var (value, _, _) = RunWithHeap(program);
        await Assert.That(value).IsEqualTo(15L)
            .Because("the promoted store must set the same bits the heap path set");
    }

    private static (int Count, long Iterations) RunLoop(long iterations) {
        var i = new Variable("i");
        var cols = new Variable("cols");
        var body = new Block([
            new Assignment(cols, new NewArray(TypeReference.To<long>(), new Constant(4L))),
            new Assignment(new IndexAccess(cols, new Constant(0L)), new Constant(1L)),
        ], [cols]);
        var program = new Block([
            new Assignment(i, new Constant(0L)),
            new ForLoop(
                new Assignment(i, new Constant(0L)),
                new LessThan(i, new Constant(iterations)),
                new Assignment(i, new Add(i, new Constant(1L))),
                body),
            i
        ], [i]);

        var (value, count, _) = RunWithHeap(program);
        return (count, value);
    }

    /// <summary>The safety half: every shape that lets the array leave the variable keeps
    /// the heap path, so nothing recycles a handle that is still reachable.</summary>
    [Test, Timeout(30_000)]
    public async Task EscapingArray_IsNotRecycled(CancellationToken ct) {
        var cols = new Variable("cols");
        var returned = new Block([
            new Assignment(cols, new NewArray(TypeReference.To<long>(), new Constant(4L))),
            new Return(cols),
        ], [cols]);
        await AssertNoRecycle(returned, "returned");
        var (returnedHandle, _, returnedSlots) = RunWithHeap(returned);
        await Assert.That(returnedHandle).IsGreaterThan(0L)
            .Because("the returned value is the array's handle");
        await Assert.That(returnedSlots[(int)returnedHandle]).IsAssignableTo<long[]>()
            .Because("the heap still holds the array the caller received");

        var alias = new Variable("alias");
        var aliased = new Block([
            new Assignment(cols, new NewArray(TypeReference.To<long>(), new Constant(4L))),
            new Assignment(alias, cols),
            alias,
        ], [cols, alias]);
        await AssertNoRecycle(aliased, "aliased");
        var (aliasHandle, _, aliasSlots) = RunWithHeap(aliased);
        await Assert.That(aliasSlots[(int)aliasHandle]).IsAssignableTo<long[]>()
            .Because("a second binding to the array is a second holder of the handle");

        var xs = new Parameter("xs", TypeReference.To<long[]>());
        var passed = new Block([
            new Assignment(cols, new NewArray(TypeReference.To<long>(), new Constant(3L))),
            new Invoke(new Lambda([xs], new Member(xs, "Length")), cols),
        ], [cols]);
        await AssertNoRecycle(passed, "passed as an argument");
        var (length, passCount, passSlots) = RunWithHeap(passed);
        await Assert.That(length).IsEqualTo(3L);
        await Assert.That(passSlots.Take(passCount).Any(slot => slot is long[])).IsTrue()
            .Because("the callee read the array, so the heap keeps it alive");
    }
}