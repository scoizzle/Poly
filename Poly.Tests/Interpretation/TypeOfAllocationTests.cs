using System.Linq.Expressions;

using Poly.Interpretation;
using Poly.Interpretation.Vm;
using Poly.Interpretation.Vm.Abi;

namespace Poly.Tests.Interpretation;

/// <summary>
/// IS7f: a <c>typeof</c> value is one heap entry per program per distinct type, seeded before
/// the body runs, so evaluating <c>typeof</c> is a read and never an allocation.
/// </summary>
public class TypeOfAllocationTests {
    private const string HandleLocalPrefix = "_type";
    private const int Iterations = 1000;

    /// <summary>One <c>typeof</c> site: the program allocates the type object once, and the
    /// evaluation itself allocates nothing.</summary>
    [Test]
    public async Task TypeOf_AllocatesOncePerProgram() {
        var tree = Build(new TypeOf(TypeReference.To<long>())).Expression;
        var calls = new HeapCalls();
        calls.Visit(tree);

        await Assert.That(calls.Allocations.Count).IsEqualTo(1)
            .Because("the type object is allocated once, at program entry");
        await Assert.That(calls.HandleReads.Count).IsEqualTo(1)
            .Because("the single typeof site reads the seeded handle");
        foreach (var read in calls.HandleReads) {
            var inner = new HeapCalls();
            inner.Visit(read);
            await Assert.That(inner.Allocations).IsEmpty()
                .Because("a typeof evaluation is a local read, not an allocation");
        }
    }

    /// <summary>A <c>typeof</c> inside a loop reads the seeded handle every iteration, so the
    /// heap does not grow with the iteration count.</summary>
    [Test]
    public async Task TypeOfInLoop_HeapDoesNotGrow() {
        var i = new Variable("i");
        var t = new Variable("t");
        var program = new Block([
            new Assignment(i, new Constant(0L)),
            new WhileLoop(
                new LessThan(i, new Constant((long)Iterations)),
                new Block([
                    new Assignment(t, new TypeOf(TypeReference.To<long>())),
                    new Assignment(i, new Add(i, new Constant(1L)))
                ], [])),
            i
        ], [i, t]);

        var counts = new List<int>();
        using var execution = Interpreter.Execute(
            Interpreter.Compile(program),
            state => {
                state.MaxLoopIterations = 100_000;
                state.DebugHook = (_, _, heap) => counts.Add(heap.Count);
            });

        await Assert.That(counts).IsNotEmpty()
            .Because("the debug hook observes the loop body");
        await Assert.That(counts.Distinct().Count()).IsEqualTo(1)
            .Because($"heap.Count is unchanging across {Iterations} iterations: "
                + $"first={counts[0]}, last={counts[^1]}");
        await Assert.That(execution.State.Heap.Count).IsEqualTo(counts[0]);
        await Assert.That(execution.GetValue<long>()).IsEqualTo(Iterations);
    }

    /// <summary>Two <c>typeof</c> sites for one type share one handle.</summary>
    [Test]
    public async Task TwoTypeOfSites_ShareOneHandle() {
        var tree = Build(new Block([
            new TypeOf(TypeReference.To<long>()),
            new TypeOf(TypeReference.To<long>())
        ], [])).Expression;
        var calls = new HeapCalls();
        calls.Visit(tree);

        await Assert.That(calls.Allocations.Count).IsEqualTo(1)
            .Because("one type, one allocation, however many sites");
        await Assert.That(calls.HandleLocalNames.Distinct().Count()).IsEqualTo(1)
            .Because("both sites read the same seeded handle");
        await Assert.That(calls.HandleReads.Count).IsEqualTo(2);

        using var execution = Interpreter.Execute(Interpreter.Compile(new Block([
            new TypeOf(TypeReference.To<long>()),
            new TypeOf(TypeReference.To<long>())
        ], [])));
        int slots = execution.State.Heap.RawSlots
            .Count(o => ReferenceEquals(o, typeof(long)));
        await Assert.That(slots).IsEqualTo(1)
            .Because("both sites resolve to the one heap entry");
    }

    /// <summary>Two distinct types get two distinct handles.</summary>
    [Test]
    public async Task DistinctTypes_GetDistinctHandles() {
        var program = new Block([
            new TypeOf(TypeReference.To<long>()),
            new TypeOf(TypeReference.To<string>())
        ], []);
        var calls = new HeapCalls();
        calls.Visit(Build(program).Expression);

        await Assert.That(calls.Allocations.Count).IsEqualTo(2);
        await Assert.That(calls.HandleLocalNames.Distinct().Count()).IsEqualTo(2)
            .Because("each distinct type gets its own seeded handle");

        using var execution = Interpreter.Execute(Interpreter.Compile(program));
        var raw = execution.State.Heap.RawSlots;
        int longSlot = Array.FindIndex(raw, o => ReferenceEquals(o, typeof(long)));
        int stringSlot = Array.FindIndex(raw, o => ReferenceEquals(o, typeof(string)));
        await Assert.That(longSlot).IsGreaterThan(0);
        await Assert.That(stringSlot).IsGreaterThan(0);
        await Assert.That(longSlot).IsNotEqualTo(stringSlot);
    }

    /// <summary>The representation changed; the meaning did not.</summary>
    [Test]
    public async Task TypeOf_StillYieldsItsType() {
        using var longExec = Interpreter.Execute(Interpreter.Compile(new TypeOf(TypeReference.To<long>())));
        await Assert.That(longExec.GetValue<Type>()).IsEqualTo(typeof(long));

        using var stringExec = Interpreter.Execute(Interpreter.Compile(new TypeOf(TypeReference.To<string>())));
        await Assert.That(stringExec.GetValue<Type>()).IsEqualTo(typeof(string));
    }

    private static AbiEmitter.BuiltVmProgram Build(Node program) =>
        AbiEmitter.Build(program, Interpreter.Analyze(program), CompilationMode.NoDebug);

    /// <summary>Collects the heap allocations and the reads of a seeded type handle.</summary>
    private sealed class HeapCalls : ExpressionVisitor {
        public List<MethodCallExpression> Allocations { get; } = [];
        public List<BinaryExpression> HandleReads { get; } = [];

        public IEnumerable<string> HandleLocalNames =>
            HandleReads.Select(r => ((ParameterExpression)r.Right).Name!);

        protected override Expression VisitMethodCall(MethodCallExpression node) {
            if (node.Method.DeclaringType == typeof(VmManagedObjectHeap)
                && node.Method.Name == nameof(VmManagedObjectHeap.Allocate))
                Allocations.Add(node);
            return base.VisitMethodCall(node);
        }

        protected override Expression VisitBinary(BinaryExpression node) {
            if (node.NodeType == ExpressionType.Assign
                && node.Right is ParameterExpression local
                && local.Name?.StartsWith(HandleLocalPrefix, StringComparison.Ordinal) == true)
                HandleReads.Add(node);
            return base.VisitBinary(node);
        }
    }
}