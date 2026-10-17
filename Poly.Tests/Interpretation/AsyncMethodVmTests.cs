using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm;
using Poly.Introspection;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Slice 2 (AA1b): an `IsAsync` AST method returns a completed `Task` / `Task<T>`, so a blocking
/// `Await` of it composes like any other awaitable. A non-`IsAsync` method is untouched.
/// </summary>
public class AsyncMethodVmTests {
    private static readonly Parameter Item = new("item", new TypeReference("Sample.Item"));
    private static readonly IReadOnlyList<object?> Receiver = [new Dictionary<string, object?>()];

    /// <summary>A host method a non-<c>IsAsync</c> AST body can return as its own task.</summary>
    public static class Host {
        public static Task<long> Completed() => Task.FromResult(5L);
    }

    private static TypeDefinitionNodeAnalyzer Analyze(TypeDefinitionNode typeNode) {
        var tda = new TypeDefinitionNodeAnalyzer();
        tda.Analyze(AnalysisContext.CreateDefault(), typeNode);
        return tda;
    }

    private static MethodDefinitionNode AsyncReturning(string name, Type task, Node body) => new(
        name, new ClrTypeReference(task), Body: body, IsAsync: true);

    private static TypeDefinitionNode ItemWith(params MethodDefinitionNode[] methods) =>
        new("Item", "Sample", Methods: methods);

    private static VmProgram Compile(TypeDefinitionNodeAnalyzer tda, Node root) =>
        Interpreter.Compile(root, tda);

    private static ExecutionResult Execute(VmProgram program) =>
        Interpreter.Execute(program, s => s.SetArgs([.. Receiver]));

    private static object? RunLong(TypeDefinitionNodeAnalyzer tda, Node root) {
        using var exec = Execute(Compile(tda, root));
        return exec.Result.IsVoid ? null : exec.GetValue<long>();
    }

    [Test]
    public async Task Await_AstAsyncMethod_ReturnsResult() {
        var tda = Analyze(ItemWith(AsyncReturning("Get", typeof(Task<long>), new Return(new Constant(42L)))));
        await Assert.That(RunLong(tda, new Await(new Invoke(new Member(Item, "Get"))))).IsEqualTo(42L);
    }

    [Test]
    public async Task Await_TwoChainedAsyncMethods() {
        var tda = Analyze(ItemWith(
            AsyncReturning("Inner", typeof(Task<long>), new Return(new Constant(7L))),
            AsyncReturning("Outer", typeof(Task<long>),
                new Return(new Await(new Invoke(new Member(new ThisReference(), "Inner")))))));
        await Assert.That(RunLong(tda, new Await(new Invoke(new Member(Item, "Outer"))))).IsEqualTo(7L);
    }

    [Test]
    public async Task Await_AstAsyncTask_IsVoid() {
        var tda = Analyze(ItemWith(
            AsyncReturning("Ping", typeof(Task), new Block([new Comment("noop")]))));
        using var exec = Execute(Compile(tda, new Await(new Invoke(new Member(Item, "Ping")))));
        await Assert.That(exec.Result.IsVoid).IsTrue();
    }

    [Test]
    public async Task Await_AstAsyncMethod_Double_UnbitsTheRingValue() {
        var tda = Analyze(ItemWith(
            AsyncReturning("GetD", typeof(Task<double>), new Return(new Constant(1.5)))));
        using var exec = Execute(Compile(tda, new Await(new Invoke(new Member(Item, "GetD")))));
        await Assert.That(exec.GetValue<double>()).IsEqualTo(1.5);
    }

    [Test]
    public async Task NonAsyncMethod_IsNotWrapped() {
        var sync = new MethodDefinitionNode(
            "Sync", new PrimitiveTypeReference(PrimitiveType.Int64),
            Body: new Return(new Constant(9L)));
        var tda = Analyze(ItemWith(sync));
        await Assert.That(RunLong(tda, new Invoke(new Member(Item, "Sync")))).IsEqualTo(9L);
    }

    [Test]
    public async Task NonAsyncTaskReturningMethod_IsNotWrapped() {
        // IsAsync false: the body's own Task<long> is the method's value, not a wrapped result.
        var raw = new MethodDefinitionNode(
            "Raw", new ClrTypeReference(typeof(Task<long>)),
            Body: new Return(new Invoke(
                new Member(new ClrTypeReference(typeof(Host)), "Completed"))));
        var tda = Analyze(ItemWith(raw));
        using var exec = Execute(Compile(tda, new Invoke(new Member(Item, "Raw"))));
        var task = exec.GetValue<Task<long>>();
        await Assert.That(task!.Result).IsEqualTo(5L);
    }

    [Test]
    public async Task AsyncTaskOfLong_FallThroughWithoutValue_IsRejected() {
        var tda = Analyze(ItemWith(
            AsyncReturning("Inner", typeof(Task<long>), new Return(new Constant(3L))),
            AsyncReturning("FallsThrough", typeof(Task<long>),
                new Block([new Invoke(new Member(new ThisReference(), "Inner"))]))));
        await Assert.That(() => Compile(tda, new Await(new Invoke(new Member(Item, "FallsThrough")))))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AwaitInNonAsyncMethod_IsRejected() {
        var bad = new MethodDefinitionNode(
            "Bad", new ClrTypeReference(typeof(Task<long>)),
            Body: new Return(new Await(new Constant(1L))));
        var tda = Analyze(ItemWith(bad));
        await Assert.That(() => Compile(tda, new Await(new Invoke(new Member(Item, "Bad")))))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("contains 'await' but is not an async method");
    }
}