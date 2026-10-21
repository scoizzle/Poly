using Poly.Interpretation;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Slice L: a module <see cref="Lambda"/> in a CLR delegate / <c>Expression&lt;D&gt;</c>
/// parameter position becomes a real nested delegate or expression tree, not a VM
/// closure handle.
/// </summary>
public class LambdaBridgeVmTests {
    private static Member Probe(string name) =>
        new(new ClrTypeReference(typeof(LambdaBridgeProbe)), name);

    [Test]
    public async Task CapturingDelegate_SeesMutationAfterCreation() {
        LambdaBridgeProbe.Reset();
        var threshold = new Variable("threshold");
        var x = new Parameter("x", TypeReference.To<int>());
        var predicate = new Lambda([x], new GreaterThan(x, threshold));
        var node = new Block([
            new Assignment(threshold, new Constant(1L)),
            new Invoke(Probe(nameof(LambdaBridgeProbe.Capture)), predicate),
            new Assignment(threshold, new Constant(10L)),
            new Invoke(Probe(nameof(LambdaBridgeProbe.Fire)), new Constant(5))
        ], [threshold]);

        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        // C# closure semantics: the delegate reads the live captured variable,
        // so 5 > 10 is false. A snapshot taken at Capture time returns true.
        await Assert.That(exec.GetValue<bool>()).IsFalse();
    }

    [Test]
    public async Task ActionBody_WriteToCapturedVariable_IsVisibleAfterCall() {
        var threshold = new Variable("threshold");
        var x = new Parameter("x", TypeReference.To<int>());
        var body = new Lambda([x], new Block([new Assignment(threshold, x)]));
        var node = new Block([
            new Assignment(threshold, new Constant(0L)),
            new Invoke(Probe(nameof(LambdaBridgeProbe.Apply)), body, new Constant(7)),
            threshold
        ], [threshold]);

        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        await Assert.That(exec.GetValue<long>()).IsEqualTo(7L);
    }

    [Test]
    public async Task DelegateInvokedAfterExecution_StateAlive_SeesLiveCapture() {
        LambdaBridgeProbe.Reset();
        var threshold = new Variable("threshold");
        var x = new Parameter("x", TypeReference.To<int>());
        var predicate = new Lambda([x], new GreaterThan(x, threshold));
        var node = new Block([
            new Assignment(threshold, new Constant(1L)),
            new Invoke(Probe(nameof(LambdaBridgeProbe.Capture)), predicate),
            threshold
        ], [threshold]);

        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        // The bridge does not police the execution window: the delegate closes over
        // the live VmState, so it still sees the captured value (5 > 1).
        await Assert.That(LambdaBridgeProbe.Fire(5)).IsTrue();
    }

    [Test]
    public async Task CaptureFree_Func_Int_Bool_PassedToClr_Invokes() {
        var x = new Parameter("x", TypeReference.To<int>());
        var predicate = new Lambda([x], new GreaterThan(x, new Constant(0L)));

        using var exec = Interpreter.Execute(Interpreter.Compile(
            new Invoke(Probe(nameof(LambdaBridgeProbe.CountIf)), predicate, new Constant(5))));

        await Assert.That(exec.GetValue<int>()).IsEqualTo(1);
    }

    [Test]
    public async Task Capturing_Func_Int_Bool_PassedToClr_SeesCapturedValue() {
        var threshold = new Variable("threshold");
        var x = new Parameter("x", TypeReference.To<int>());
        var predicate = new Lambda([x], new GreaterThan(x, threshold));
        var node = new Block([
            new Assignment(threshold, new Constant(2L)),
            new Invoke(Probe(nameof(LambdaBridgeProbe.CountIf)), predicate, new Constant(1))
        ], [threshold]);

        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        await Assert.That(exec.GetValue<int>()).IsEqualTo(0);
    }

    [Test]
    public async Task Expression_Func_Int_Bool_PassedToQueryable_Executes() {
        var x = new Parameter("x", TypeReference.To<int>());
        var predicate = new Lambda([x], new GreaterThan(x, new Constant(1L)));
        var query = new Invoke(Probe(nameof(LambdaBridgeProbe.InMemoryQuery)));

        using var exec = Interpreter.Execute(Interpreter.Compile(
            new Invoke(Probe(nameof(LambdaBridgeProbe.Any)), query, predicate)));

        await Assert.That(exec.GetValue<bool>()).IsTrue();
    }

    [Test]
    public async Task CapturingExpression_Func_Int_Bool_PassedToQueryable_SeesCapturedValue() {
        var threshold = new Variable("threshold");
        var x = new Parameter("x", TypeReference.To<int>());
        var predicate = new Lambda([x], new GreaterThan(x, threshold));
        var query = new Invoke(Probe(nameof(LambdaBridgeProbe.InMemoryQuery)));
        var node = new Block([
            new Assignment(threshold, new Constant(2L)),
            new Invoke(Probe(nameof(LambdaBridgeProbe.Any)), query, predicate)
        ], [threshold]);

        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        await Assert.That(exec.GetValue<bool>()).IsTrue();
    }

    [Test]
    public async Task Action_Int_Consumer_ForEach_RunsBody() {
        var source = new Variable("source");
        var sink = new Variable("sink");
        var x = new Parameter("x", TypeReference.To<int>());
        var body = new Lambda([x], new Invoke(new Member(sink, nameof(List<int>.Add)), x));
        var node = new Block([
            new Assignment(source, new New(new ClrTypeReference(typeof(List<int>)))),
            new Assignment(sink, new New(new ClrTypeReference(typeof(List<int>)))),
            new Invoke(new Member(source, nameof(List<int>.Add)), new Constant(1)),
            new Invoke(new Member(source, nameof(List<int>.Add)), new Constant(2)),
            new Invoke(new Member(source, nameof(List<int>.Add)), new Constant(3)),
            new Invoke(new Member(source, nameof(List<int>.ForEach)), body),
            sink
        ], [source, sink]);

        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        await Assert.That(exec.GetValue<List<int>>()).IsEquivalentTo([1, 2, 3]);
    }

    [Test]
    public async Task ValueBody_ToAction_Throws() {
        var list = new Variable("list");
        var x = new Parameter("x", TypeReference.To<int>());
        var body = new Lambda([x], new Add(x, new Constant(1L)));
        var node = new Block([
            new Assignment(list, new New(new ClrTypeReference(typeof(List<int>)))),
            new Invoke(new Member(list, nameof(List<int>.ForEach)), body)
        ], [list]);

        await Assert.That(() => Interpreter.Compile(node))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("value-form lambda body cannot convert to the void delegate");
    }

    [Test]
    public async Task ImpureExpressionBody_Throws() {
        var x = new Parameter("x", TypeReference.To<int>());
        var created = new New(new ClrTypeReference(typeof(List<int>)));
        var body = new Lambda([x], new Equal(new Member(created, nameof(List<int>.Count)), x));
        var query = new Invoke(Probe(nameof(LambdaBridgeProbe.InMemoryQuery)));

        await Assert.That(() => Interpreter.Compile(
            new Invoke(Probe(nameof(LambdaBridgeProbe.Any)), query, body)))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("not a pure CLR expression");
    }

    [Test]
    public async Task StoredDelegate_FromVariable_PassedToClr_Invokes() {
        var threshold = new Variable("threshold");
        var declaration = new Variable("pred");
        var x = new Parameter("x", TypeReference.To<int>());
        var predicate = new Lambda([x], new GreaterThan(x, threshold));
        var node = new Block([
            new Assignment(threshold, new Constant(2L)),
            new Assignment(declaration, predicate),
            // A reader emits a fresh Variable per identifier; use a distinct node so
            // the binding lookup is exercised, not just a shared declaration node.
            new Invoke(Probe(nameof(LambdaBridgeProbe.CountIf)), new Variable("pred"), new Constant(1))
        ], [threshold, declaration]);

        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        await Assert.That(exec.GetValue<int>()).IsEqualTo(0);
    }

    [Test]
    public async Task StoredExpressionLambda_FromVariable_PassedToQueryable_Quotes() {
        var threshold = new Variable("threshold");
        var declaration = new Variable("pred");
        var x = new Parameter("x", TypeReference.To<int>());
        var predicate = new Lambda([x], new GreaterThan(x, threshold));
        var query = new Invoke(Probe(nameof(LambdaBridgeProbe.InMemoryQuery)));
        var node = new Block([
            new Assignment(threshold, new Constant(2L)),
            new Assignment(declaration, predicate),
            new Invoke(Probe(nameof(LambdaBridgeProbe.Any)), query, new Variable("pred"))
        ], [threshold, declaration]);

        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        await Assert.That(exec.GetValue<bool>()).IsTrue();
    }

    [Test]
    public async Task HostExpressionReturnedFromClr_PassedToQueryable_PassesThrough() {
        var query = new Invoke(Probe(nameof(LambdaBridgeProbe.InMemoryQuery)));
        var host = new Invoke(Probe(nameof(LambdaBridgeProbe.HostExpression)));

        using var exec = Interpreter.Execute(Interpreter.Compile(
            new Invoke(Probe(nameof(LambdaBridgeProbe.Any)), query, host)));

        await Assert.That(exec.GetValue<bool>()).IsTrue();
    }

    [Test]
    public async Task NullExpressionArgument_Compiles() {
        var query = new Invoke(Probe(nameof(LambdaBridgeProbe.InMemoryQuery)));

        await Assert.That(() => Interpreter.Compile(
            new Invoke(Probe(nameof(LambdaBridgeProbe.Any)), query, new Constant(null))))
            .ThrowsNothing();
    }

    [Test]
    public async Task ConditionallyReassignedStoredExpression_Throws() {
        var declaration = new Variable("pred");
        var x = new Parameter("x", TypeReference.To<int>());
        var first = new Lambda([x], new GreaterThan(x, new Constant(100L)));
        var second = new Lambda([x], new GreaterThan(x, new Constant(1L)));
        var query = new Invoke(Probe(nameof(LambdaBridgeProbe.InMemoryQuery)));
        var node = new Block([
            new Assignment(declaration, first),
            new IfStatement(new Constant(true), new Assignment(new Variable("pred"), second)),
            new Invoke(Probe(nameof(LambdaBridgeProbe.Any)), query, new Variable("pred"))
        ], [declaration]);

        await Assert.That(() => Interpreter.Compile(node))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("reassigned or conditional");
    }

    [Test]
    public async Task ProducedStoredLambda_IsNotQuoted() {
        var declaration = new Variable("pred");
        var x = new Parameter("x", TypeReference.To<int>());
        var producer = new Lambda([], new Lambda([x], new GreaterThan(x, new Constant(1L))));
        var query = new Invoke(Probe(nameof(LambdaBridgeProbe.InMemoryQuery)));
        var node = new Block([
            new Assignment(declaration, new Invoke(producer)),
            new Invoke(Probe(nameof(LambdaBridgeProbe.Any)), query, new Variable("pred"))
        ], [declaration]);

        await Assert.That(() => Interpreter.Compile(node))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("reassigned or conditional");
    }

    [Test]
    public async Task ProducedStoredLambda_AtDelegatePosition_UsesTheLiveClosure() {
        var declaration = new Variable("pred");
        var x = new Parameter("x", TypeReference.To<int>());
        var producer = new Lambda([], new Lambda([x], new GreaterThan(x, new Constant(0L))));
        var node = new Block([
            new Assignment(declaration, new Invoke(producer)),
            new Invoke(Probe(nameof(LambdaBridgeProbe.CountIf)), new Variable("pred"), new Constant(5))
        ], [declaration]);

        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        await Assert.That(exec.GetValue<int>()).IsEqualTo(1);
    }

    [Test]
    public async Task CapturedReassignmentOfStoredExpression_Throws() {
        var declaration = new Variable("pred");
        var x = new Parameter("x", TypeReference.To<int>());
        var first = new Lambda([x], new GreaterThan(x, new Constant(100L)));
        var second = new Lambda([x], new GreaterThan(x, new Constant(1L)));
        var mutator = new Lambda([], new Assignment(new Variable("pred"), second));
        var query = new Invoke(Probe(nameof(LambdaBridgeProbe.InMemoryQuery)));
        var node = new Block([
            new Assignment(declaration, first),
            new Invoke(mutator),
            new Invoke(Probe(nameof(LambdaBridgeProbe.Any)), query, new Variable("pred"))
        ], [declaration]);

        await Assert.That(() => Interpreter.Compile(node))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("reassigned or conditional");
    }

    [Test]
    public async Task ConditionallyReassignedStoredDelegate_UsesTheLiveClosure() {
        var declaration = new Variable("pred");
        var x = new Parameter("x", TypeReference.To<int>());
        var first = new Lambda([x], new GreaterThan(x, new Constant(100L)));
        var second = new Lambda([x], new GreaterThan(x, new Constant(0L)));
        var node = new Block([
            new Assignment(declaration, first),
            new IfStatement(new Constant(true), new Assignment(new Variable("pred"), second)),
            new Invoke(Probe(nameof(LambdaBridgeProbe.CountIf)), new Variable("pred"), new Constant(5))
        ], [declaration]);

        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        await Assert.That(exec.GetValue<int>()).IsEqualTo(1);
    }

    [Test]
    public async Task ForwardedHostDelegate_ThroughInlineParameter_Invokes() {
        var pred = new Parameter("pred", TypeReference.To<Func<int, bool>>());
        var body = new Lambda([pred], new Invoke(
            Probe(nameof(LambdaBridgeProbe.CountIf)),
            new Variable("pred"),
            new Constant(5)));
        var host = new Invoke(Probe(nameof(LambdaBridgeProbe.HostPredicate)));

        using var exec = Interpreter.Execute(Interpreter.Compile(new Invoke(body, host)));

        await Assert.That(exec.GetValue<int>()).IsEqualTo(1);
    }

    [Test]
    public async Task DelegateReturningDouble_BitcastsTheIeeeSlot() {
        var x = new Parameter("x", TypeReference.To<int>());
        // A computed double body (not a constant) exercises the body metadata path.
        var body = new Lambda([x], new Add(new Constant(1.5), new Constant(0.5)));
        using var exec = Interpreter.Execute(Interpreter.Compile(
            new Invoke(Probe(nameof(LambdaBridgeProbe.ApplyDouble)), body, new Constant(3))));

        await Assert.That(exec.GetValue<double>()).IsEqualTo(2.0);
    }

    [Test]
    public async Task NestedExpressionCapture_QuotesFromTheCell() {
        var threshold = new Variable("threshold");
        var x = new Parameter("x", TypeReference.To<int>());
        var inner = new Lambda([x], new GreaterThan(x, threshold));
        var any = new Invoke(Probe(nameof(LambdaBridgeProbe.Any)),
            new Invoke(Probe(nameof(LambdaBridgeProbe.InMemoryQuery))), inner);
        var outer = new Lambda([], any);
        var node = new Block([
            new Assignment(threshold, new Constant(2L)),
            new Invoke(outer)
        ], [threshold]);

        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        await Assert.That(exec.GetValue<bool>()).IsTrue();
    }

    [Test]
    public async Task StoredLambda_DistinctUseNode_InvokeResolvesAndRuns() {
        // A reader emits a fresh Variable per identifier: the invoke's callee is not
        // the declaration node. The stored lambda must resolve through the binding.
        var declaration = new Variable("fn");
        var x = new Parameter("x", TypeReference.To<long>());
        var node = new Block([
            new Assignment(declaration, new Lambda([x], new Add(x, new Constant(1L)))),
            new Invoke(new Variable("fn"), new Constant(6L))
        ], [declaration]);

        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        await Assert.That(exec.GetValue<long>()).IsEqualTo(7L);
    }

    [Test]
    public async Task ForInitStoredLambda_ReassignedInBody_IsNotQuoted() {
        var declaration = new Variable("pred");
        var x = new Parameter("x", TypeReference.To<int>());
        var first = new Lambda([x], new GreaterThan(x, new Constant(100L)));
        var second = new Lambda([x], new GreaterThan(x, new Constant(1L)));
        var query = new Invoke(Probe(nameof(LambdaBridgeProbe.InMemoryQuery)));
        var loop = new ForLoop(
            new Assignment(declaration, first),
            new Constant(true),
            null,
            new Block([
                new Assignment(new Variable("pred"), second),
                new Invoke(Probe(nameof(LambdaBridgeProbe.Any)), query, new Variable("pred")),
                new BreakStatement()
            ]),
            DeclaredVariables: [declaration]);

        await Assert.That(() => Interpreter.Compile(loop))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("reassigned or conditional");
    }

    [Test]
    public async Task ForInitStoredLambda_SoleLambdaAtExpressionPosition_IsQuoted() {
        var declaration = new Variable("pred");
        var x = new Parameter("x", TypeReference.To<int>());
        var predicate = new Lambda([x], new GreaterThan(x, new Constant(1L)));
        var query = new Invoke(Probe(nameof(LambdaBridgeProbe.InMemoryQuery)));
        var loop = new ForLoop(
            new Assignment(declaration, predicate),
            new Constant(false),
            null,
            new Invoke(Probe(nameof(LambdaBridgeProbe.Any)), query, new Variable("pred")),
            DeclaredVariables: [declaration]);

        // A sole for-init lambda is the AST value at that position, so it quotes.
        _ = Interpreter.Compile(loop);
    }


    public static class LambdaBridgeProbe {
        private static Func<int, bool>? _captured;

        public static void Capture(Func<int, bool> predicate) => _captured = predicate;

        public static bool Fire(int value) => _captured!(value);

        public static double ApplyDouble(Func<int, double> body, int value) => body(value);

        public static void Reset() => _captured = null;

        public static void Apply(Action<int> action, int value) => action(value);

        public static int CountIf(Func<int, bool> predicate, int value) =>
            predicate(value) ? 1 : 0;

        public static IQueryable<int> InMemoryQuery() =>
            new[] { 1, 2, 3 }.AsQueryable();

        public static bool Any(
            IQueryable<int> source,
            System.Linq.Expressions.Expression<Func<int, bool>> predicate) =>
            source.Any(predicate);

        public static System.Linq.Expressions.Expression<Func<int, bool>> HostExpression() =>
            x => x > 1;

        public static Func<int, bool> HostPredicate() => x => x > 0;
    }
}