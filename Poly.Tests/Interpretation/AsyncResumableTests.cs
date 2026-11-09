using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm;
using Poly.Introspection;

namespace Poly.Tests.Interpretation;

/// <summary>
/// #337 slice 2: an <c>IsAsync</c> AST method whose awaits sit at statement positions runs on a
/// private, resumable frame. <c>await</c> of an incomplete task returns control to the caller with
/// the method's own incomplete task, and the task's continuation resumes the body. Resume uses the
/// same hierarchical dispatch as iterators (#338).
/// </summary>
public class AsyncResumableTests {
    private const string Type = "Sample.Item";
    private static readonly TimeSpan Blocked = TimeSpan.FromSeconds(5);

    private static ITypeDefinitionProvider Analyze(params MethodDefinitionNode[] methods) {
        var tda = new TypeDefinitionNodeAnalyzer();
        var ctx = AnalysisContext.CreateDefault();
        tda.Analyze(ctx, new TypeDefinitionNode("Item", "Sample", Methods: methods));
        return ctx.TypeDefinitions;
    }

    private static Parameter TaskParameter(string name, Type task) => new(name, new ClrTypeReference(task));

    private static MethodDefinitionNode Async(string name, Type task, Parameter[] parameters, Node body) => new(
        name, new ClrTypeReference(task), parameters, body, IsStatic: true, IsAsync: true);

    private static Invoke Call(string method, params Node[] arguments) =>
        new(new Member(new TypeReference(Type), method), arguments);

    private static readonly Constant Zero = new(0L);

    /// <summary>Executes on a worker so that a body which blocks on an incomplete await fails the
    /// test with a timeout instead of hanging it; <paramref name="release"/> frees the blocked worker.</summary>
    private static ExecutionResult Run(
        ITypeDefinitionProvider tda, Node root, Action release) {
        var program = Interpreter.Compile(root, tda);
        var worker = Task.Run(() => Interpreter.Execute(program));
        if (!worker.Wait(Blocked)) {
            release();
            throw new TimeoutException(
                "the call did not return: await blocked the caller instead of suspending the method");
        }
        return worker.Result;
    }

    [Test]
    public async Task Async_FirstAwaitReturnsIncompleteTask() {
        var gate = new TaskCompletionSource<long>();
        var g = TaskParameter("gate", typeof(Task<long>));
        var v = new Variable("v");
        var tda = Analyze(Async("Get", typeof(Task<long>), [g], new Block([
            new Assignment(v, new Await(g)),
            new Return(new Add(v, new Constant(1L))),
        ], [v])));

        using var exec = Run(tda, Call("Get", new Constant(gate.Task)), () => gate.SetResult(0));
        var task = exec.GetValue<Task<long>>()!;

        await Assert.That(task.IsCompleted).IsFalse();
        gate.SetResult(41L);
        await Assert.That(task.IsCompletedSuccessfully).IsTrue();
        await Assert.That(task.Result).IsEqualTo(42L);
    }

    [Test]
    public async Task Async_MultipleAwaits_ResumeInOrder_LocalsPreserved() {
        var first = new TaskCompletionSource<long>();
        var second = new TaskCompletionSource<long>();
        var g1 = TaskParameter("g1", typeof(Task<long>));
        var g2 = TaskParameter("g2", typeof(Task<long>));
        var k = new Variable("k");
        var a = new Variable("a");
        var b = new Variable("b");
        var tda = Analyze(Async("Two", typeof(Task<long>), [g1, g2], new Block([
            new Assignment(k, new Constant(7L)),
            new Assignment(a, new Await(g1)),
            new Assignment(k, new Add(k, a)),
            new Assignment(b, new Await(g2)),
            new Return(new Add(new Multiply(k, new Constant(100L)), b)),
        ], [k, a, b])));

        using var exec = Run(tda, Call("Two", new Constant(first.Task), new Constant(second.Task)),
            () => { first.TrySetResult(0); second.TrySetResult(0); });
        var task = exec.GetValue<Task<long>>()!;

        await Assert.That(task.IsCompleted).IsFalse();
        first.SetResult(3L);
        await Assert.That(task.IsCompleted).IsFalse();
        second.SetResult(5L);
        // k = 7 + 3 = 10, kept across both suspensions; then 10 * 100 + 5.
        await Assert.That(task.Result).IsEqualTo(1005L);
    }

    [Test]
    public async Task Async_TwoMethodsInterleave() {
        var gateA = new TaskCompletionSource<long>();
        var gateB = new TaskCompletionSource<long>();
        var ga = TaskParameter("ga", typeof(Task<long>));
        var gb = TaskParameter("gb", typeof(Task<long>));
        var g = TaskParameter("gate", typeof(Task<long>));
        var ta = new Variable("ta");
        var tb = new Variable("tb");
        var ra = new Variable("ra");
        var rb = new Variable("rb");
        var tda = Analyze(
            Async("Wait", typeof(Task<long>), [g], new Return(new Await(g))),
            Async("Both", typeof(Task<long>), [ga, gb], new Block([
                new Assignment(ta, Call("Wait", ga)),
                new Assignment(tb, Call("Wait", gb)),
                new Assignment(ra, new Await(ta)),
                new Assignment(rb, new Await(tb)),
                new Return(new Add(ra, rb)),
            ], [ta, tb, ra, rb])));

        using var exec = Run(tda, Call("Both", new Constant(gateA.Task), new Constant(gateB.Task)),
            () => { gateA.TrySetResult(0); gateB.TrySetResult(0); });
        var task = exec.GetValue<Task<long>>()!;

        // Both inner calls were started before either gate opened; B finishes first.
        gateB.SetResult(20L);
        await Assert.That(task.IsCompleted).IsFalse();
        gateA.SetResult(1L);
        await Assert.That(task.Result).IsEqualTo(21L);
    }

    [Test]
    public async Task Async_AwaitNestedCall_Resumes() {
        var gate = new TaskCompletionSource<long>();
        var g = TaskParameter("gate", typeof(Task<long>));
        var h = TaskParameter("h", typeof(Task<long>));
        var r = new Variable("r");
        var i = new Variable("i");
        var tda = Analyze(
            Async("Inner", typeof(Task<long>), [g], new Block([
                new Assignment(i, new Await(g)),
                new Return(new Add(i, new Constant(1L))),
            ], [i])),
            Async("Outer", typeof(Task<long>), [h], new Block([
                new Assignment(r, new Await(Call("Inner", h))),
                new Return(new Add(r, new Constant(10L))),
            ], [r])));

        using var exec = Run(tda, Call("Outer", new Constant(gate.Task)), () => gate.SetResult(0));
        var task = exec.GetValue<Task<long>>()!;

        await Assert.That(task.IsCompleted).IsFalse();
        gate.SetResult(5L);
        await Assert.That(task.Result).IsEqualTo(16L);
    }

    [Test]
    public async Task Async_CompletedAwait_DoesNotSuspend() {
        var g = TaskParameter("gate", typeof(Task<long>));
        var tda = Analyze(Async("Get", typeof(Task<long>), [g], new Return(new Await(g))));

        using var exec = Run(tda, Call("Get", new Constant(Task.FromResult(9L))), () => { });
        var task = exec.GetValue<Task<long>>()!;

        await Assert.That(task.IsCompletedSuccessfully).IsTrue();
        await Assert.That(task.Result).IsEqualTo(9L);
    }

    [Test]
    public async Task Async_FaultedAwait_FaultsTheMethodTask() {
        var gate = new TaskCompletionSource<long>();
        var g = TaskParameter("gate", typeof(Task<long>));
        var tda = Analyze(Async("Get", typeof(Task<long>), [g], new Return(new Await(g))));

        using var exec = Run(tda, Call("Get", new Constant(gate.Task)), () => gate.SetResult(0));
        var task = exec.GetValue<Task<long>>()!;

        await Assert.That(task.IsCompleted).IsFalse();
        gate.SetException(new FormatException("gate failed"));
        await Assert.That(task.IsFaulted).IsTrue();
        await Assert.That(task.Exception!.InnerException).IsTypeOf<FormatException>();
        await Assert.That(task.Exception!.InnerException!.Message).IsEqualTo("gate failed");
    }

    [Test]
    public async Task Async_AlreadyFaultedAwait_ReturnsFaultedTask_NotASynchronousThrow() {
        var g = TaskParameter("gate", typeof(Task<long>));
        var tda = Analyze(Async("Get", typeof(Task<long>), [g], new Return(new Await(g))));

        using var exec = Run(tda,
            Call("Get", new Constant(Task.FromException<long>(new FormatException("already")))), () => { });
        var task = exec.GetValue<Task<long>>()!;

        await Assert.That(task.IsFaulted).IsTrue();
        await Assert.That(task.Exception!.InnerException!.Message).IsEqualTo("already");
    }

    [Test]
    public async Task Async_VoidTask_SuspendsAndCompletes() {
        var gate = new TaskCompletionSource<long>();
        var g = TaskParameter("gate", typeof(Task<long>));
        var tda = Analyze(Async("Ping", typeof(Task), [g], new Block([new Await(g)])));

        using var exec = Run(tda, Call("Ping", new Constant(gate.Task)), () => gate.SetResult(0));
        var task = exec.GetValue<Task>()!;

        await Assert.That(task.IsCompleted).IsFalse();
        gate.SetResult(1L);
        await Assert.That(task.IsCompletedSuccessfully).IsTrue();
    }

    [Test]
    public async Task Async_AwaitInsideTry_StaysBlocking_AndStillWorks() {
        // Slice 3: an await inside an exception region keeps the method in blocking mode.
        var g = TaskParameter("gate", typeof(Task<long>));
        var x = new Variable("x");
        var tda = Analyze(Async("Get", typeof(Task<long>), [g], new Block([
            new Assignment(x, new Constant(0L)),
            new TryCatchFinally(new Assignment(x, new Await(g))),
            new Return(x),
        ], [x])));
        using var exec = Run(tda, Call("Get", new Constant(Task.FromResult(4L))), () => { });
        await Assert.That(exec.GetValue<Task<long>>()!.Result).IsEqualTo(4L);
    }

    [Test]
    public async Task AsyncVoid_WithAwait_IsRejectedWithAName() {
        var g = TaskParameter("gate", typeof(Task<long>));
        var bad = new MethodDefinitionNode(
            "Bad", new ClrTypeReference(typeof(void)), [g],
            new Block([new Await(g)]), IsStatic: true, IsAsync: true);
        var tda = Analyze(bad);
        await Assert.That(() => Interpreter.Compile(Call("Bad", new Constant(Task.FromResult(1L))), tda))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("contains 'await' but is not an async method returning Task or Task<T>");
    }

    [Test]
    public async Task Await_NonAwaitableInAsyncMethod_IsRejectedWithAName() {
        var tda = Analyze(Async("Bad", typeof(Task<long>), [], new Return(new Await(new Constant(1L)))));
        await Assert.That(() => Interpreter.Compile(Call("Bad"), tda))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("is not awaitable");
    }

    [Test]
    public async Task Async_ResumeAfterStateDisposed_FaultsTheTask() {
        var gate = new TaskCompletionSource<long>();
        var g = TaskParameter("gate", typeof(Task<long>));
        var tda = Analyze(Async("Get", typeof(Task<long>), [g], new Return(new Await(g))));

        var exec = Run(tda, Call("Get", new Constant(gate.Task)), () => gate.SetResult(0));
        var task = exec.GetValue<Task<long>>()!;
        exec.Dispose();
        gate.SetResult(1L);

        await Assert.That(task.IsFaulted).IsTrue();
        await Assert.That(task.Exception!.InnerException).IsTypeOf<ObjectDisposedException>();
    }

    [Test]
    public async Task Async_AwaitInExpressionPosition_StaysBlocking_AndStillWorks() {
        var g = TaskParameter("gate", typeof(Task<long>));
        var tda = Analyze(Async("Get", typeof(Task<long>), [g],
            new Return(new Add(new Await(g), new Constant(1L)))));
        using var exec = Run(tda, Call("Get", new Constant(Task.FromResult(4L))), () => { });
        await Assert.That(exec.GetValue<Task<long>>()!.Result).IsEqualTo(5L);
    }

    [Test]
    public async Task AsyncTaskOfT_WithoutReturnOnEveryPath_StaysBlocking() {
        // The block's last expression is the value, as before: not eligible to suspend, still correct.
        var g = TaskParameter("gate", typeof(Task<long>));
        var tda = Analyze(Async("Get", typeof(Task<long>), [g], new Block([new Await(g)])));
        using var exec = Run(tda, Call("Get", new Constant(Task.FromResult(6L))), () => { });
        await Assert.That(exec.GetValue<Task<long>>()!.Result).IsEqualTo(6L);
    }

    [Test]
    public async Task AsyncTaskReturningAValue_StaysBlocking() {
        var g = TaskParameter("gate", typeof(Task<long>));
        var tda = Analyze(Async("Bad", typeof(Task), [g], new Block([new Await(g), new Return(new Constant(1L))])));
        // Not eligible to suspend (it returns a value), so the blocking path decides, as before.
        using var exec = Run(tda, Call("Bad", new Constant(Task.FromResult(1L))), () => { });
        await Assert.That(exec.GetValue<Task>()!.IsCompletedSuccessfully).IsTrue();
    }
}