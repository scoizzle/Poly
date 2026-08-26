using Poly.Interpretation;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Interpretation;

public class InterpretationStabilizationTests {
    [Test]
    public async Task CompileChecked_MissingMember_Throws() {
        var analyzer = AnalyzePerson();
        var entity = new Parameter("entity", new TypeReference("Person"));
        await Assert.That(() =>
            Interpreter.CompileChecked(new Member(entity, "Nope"), analyzer)
        ).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Execute_UnresolvedMemberRead_Throws() {
        var e = new Parameter("entity");
        await Assert.That(() => Interpreter.Compile(new Member(e, "Age")))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task VmForEach_IntArray_SumsItems() {
        var sum = new Variable("sum");
        var item = new Variable("item");
        var node = new Block([
            new Assignment(sum, new Constant(0L)),
            new ForEachLoop(
                item,
                new Constant(new[] { 1, 2, 3, 4 }),
                new Assignment(sum, new Add(sum, item))),
            sum
        ], [sum]);
        var program = Interpreter.Compile(node);
        using var exec = Interpreter.Execute(program);
        await Assert.That(exec.Result.GetValue<long>()).IsEqualTo(10L);
    }

    [Test]
    public async Task VmForEach_HashSet_SumsItems() {
        var sum = new Variable("sum");
        var item = new Variable("item");
        var node = new Block([
            new Assignment(sum, new Constant(0L)),
            new ForEachLoop(
                item,
                new Constant(new HashSet<int> { 1, 2, 3 }),
                new Assignment(sum, new Add(sum, item))),
            sum
        ], [sum]);
        var program = Interpreter.Compile(node);
        using var exec = Interpreter.Execute(program);
        await Assert.That(exec.Result.GetValue<long>()).IsEqualTo(6L);
    }

    [Test]
    public async Task VmForEach_NullCollection_Throws() {
        var item = new Variable("item");
        var node = new ForEachLoop(item, new Constant(null!), new Constant(0L));
        var program = Interpreter.Compile(node);
        await Assert.That(() => {
            using var exec = Interpreter.Execute(program);
        }).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Switch_NonTakenCase_DoesNotRun() {
        var taken = new Variable("taken");
        var skipped = new Variable("skipped");
        var node = new Block([
            new Assignment(taken, new Constant(0L)),
            new Assignment(skipped, new Constant(0L)),
            new SwitchStatement(
                new Constant(1L),
                [
                    new SwitchCase(new Constant(1L),
                        new Assignment(taken, new Constant(1L))),
                    new SwitchCase(new Constant(2L),
                        new Assignment(skipped, new Constant(1L)))
                ],
                new Constant(0L)),
            skipped
        ], [taken, skipped]);
        var program = Interpreter.Compile(node);
        using var exec = Interpreter.Execute(program);
        await Assert.That(exec.Result.GetValue<long>()).IsEqualTo(0L);
    }

    [Test]
    public async Task Using_DisposesResource() {
        var resource = new TrackingDisposable();
        var node = new UsingStatement(new Constant(resource), new Constant(1L));
        var program = Interpreter.Compile(node);
        using var exec = Interpreter.Execute(program);
        await Assert.That(resource.Disposed).IsTrue();
    }

    [Test]
    public async Task Coalesce_NullMember_TakesRight() {
        var bag = new Dictionary<string, object?> { ["Ref"] = null };
        var type = PersonTypeDef();
        var analyzer = Analyze(type);
        var entity = new Parameter("entity", new TypeReference("Person"));
        var node = new Coalesce(new Member(entity, "Ref"), new Constant("fallback"));
        var program = Interpreter.CompileChecked(node, analyzer);
        using var exec = Interpreter.Execute(program, s => s.SetArgs(new object?[] { bag }));
        await Assert.That(exec.Result.GetValue<string>()).IsEqualTo("fallback");
    }

    [Test, Timeout(10_000)]
    public async Task ForLoop_Continue_RunsIncrement(CancellationToken ct) {
        var sum = new Variable("sum");
        var i = new Variable("i");
        var node = new Block([
            new Assignment(sum, new Constant(0L)),
            new ForLoop(
                new Assignment(i, new Constant(0L)),
                new LessThan(i, new Constant(5L)),
                new Assignment(i, new Add(i, new Constant(1L))),
                new Block([
                    new IfStatement(
                        new LessThan(i, new Constant(2L)),
                        new ContinueStatement(null)),
                    new Assignment(sum, new Add(sum, new Constant(1L)))
                ]),
                null),
            sum
        ], [sum, i]);
        var program = Interpreter.Compile(node);
        using var exec = Interpreter.Execute(program, s => s.MaxLoopIterations = 10_000);
        await Assert.That(exec.Result.GetValue<long>()).IsEqualTo(3L);
    }

    [Test]
    public async Task CollectionOfAstType_GetElementType_IsTarget() {
        var target = new TypeDefinitionNode("Target",
            Properties: [
                new PropertyDefinitionNode("Age",
                    new PrimitiveTypeReference(PrimitiveType.Int64),
                    Getter: new PropertyGetterDefinitionNode())
            ]);
        var source = new TypeDefinitionNode("Source",
            Properties: [
                new PropertyDefinitionNode("Items",
                    new CollectionTypeReference(new TypeReference("Target")),
                    Getter: new PropertyGetterDefinitionNode())
            ]);
        var analyzer = new TypeDefinitionNodeAnalyzer();
        var ctx = AnalysisContext.CreateDefault();
        analyzer.Analyze(ctx, target);
        analyzer.Analyze(ctx, source);
        var items = analyzer.GetTypeDefinition("Source")!.Properties.Single(p => p.Name == "Items");
        var element = items.MemberTypeDefinition.GetElementType();
        await Assert.That(element).IsNotNull();
        await Assert.That(element!.Name).IsEqualTo("Target");
    }

    [Test]
    public async Task UnknownTypeReference_ThrowsOnResolve() {
        var type = new TypeDefinitionNode("Holder",
            Properties: [
                new PropertyDefinitionNode("X",
                    new TypeReference("Missing"),
                    Getter: new PropertyGetterDefinitionNode())
            ]);
        var analyzer = new TypeDefinitionNodeAnalyzer();
        var ctx = AnalysisContext.CreateDefault();
        analyzer.Analyze(ctx, type);
        var prop = analyzer.GetTypeDefinition("Holder")!.Properties.Single();
        await Assert.That(() => _ = prop.MemberTypeDefinition)
            .Throws<InvalidOperationException>();
    }

    private static TypeDefinitionNodeAnalyzer AnalyzePerson() => Analyze(PersonTypeDef());

    private static TypeDefinitionNode PersonTypeDef() => new(
        "Person",
        Properties: [
            new PropertyDefinitionNode("Age",
                new PrimitiveTypeReference(PrimitiveType.Int64),
                Getter: new PropertyGetterDefinitionNode()),
            new PropertyDefinitionNode("Name",
                new PrimitiveTypeReference(PrimitiveType.String),
                Getter: new PropertyGetterDefinitionNode()),
            new PropertyDefinitionNode("Ref",
                new PrimitiveTypeReference(PrimitiveType.Structure),
                Getter: new PropertyGetterDefinitionNode())
        ]);

    private static TypeDefinitionNodeAnalyzer Analyze(TypeDefinitionNode node) {
        var analyzer = new TypeDefinitionNodeAnalyzer();
        analyzer.Analyze(AnalysisContext.CreateDefault(), node);
        return analyzer;
    }

    [Test]
    public async Task ThisReference_AfterSetArgs_ReturnsInstance() {
        var instance = new object();
        var x = new Variable("x");
        var node = new Block([
            new Assignment(x, new Constant(1L)),
            new ThisReference()
        ], [x]);
        var program = Interpreter.Compile(node);
        using var exec = Interpreter.Execute(program, s => s.SetArgs(instance));
        await Assert.That(exec.GetValue<object>()).IsSameReferenceAs(instance);
    }

    [Test]
    public async Task GetValue_NullConstant_IsNull() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new Constant(null)));
        await Assert.That(exec.GetValue<object>()).IsNull();
    }

    [Test]
    public async Task GetValue_DoubleConstant_IsBitcast() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new Constant(1.5)));
        await Assert.That(exec.GetValue<double>()).IsEqualTo(1.5);
    }

    [Test]
    public async Task GetValue_ZeroLong_IsZeroNotNull() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new Constant(0L)));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(0L);
        await Assert.That(exec.GetValue<object>()).IsEqualTo(0L);
    }

    [Test, Timeout(10_000)]
    public async Task MaxLoopIterations_WhileTrue_Throws(CancellationToken ct) {
        await AssertExceedsLoopLimit(new WhileLoop(new Constant(true), new Constant(0L)));
    }

    [Test, Timeout(10_000)]
    public async Task MaxLoopIterations_DoWhileTrue_Throws(CancellationToken ct) {
        await AssertExceedsLoopLimit(new DoWhileLoop(new Constant(0L), new Constant(true)));
    }

    [Test, Timeout(10_000)]
    public async Task MaxLoopIterations_ForTrue_Throws(CancellationToken ct) {
        await AssertExceedsLoopLimit(new ForLoop(null, new Constant(true), null, new Constant(0L)));
    }

    [Test, Timeout(10_000)]
    public async Task MaxLoopIterations_ForEachInfinite_Throws(CancellationToken ct) {
        var item = new Variable("item");
        await AssertExceedsLoopLimit(new ForEachLoop(
            item, new Constant(Forever(1)), new Constant(0L)));
    }

    [Test]
    public async Task MaxLoopIterations_CountsHeaderVisits_NIterationsNeedNPlusOne() {
        var i = new Variable("i");
        var node = CountToThree(i);
        var program = Interpreter.Compile(node);
        await Assert.That(() => {
            using var exec = Interpreter.Execute(program, s => s.MaxLoopIterations = 3);
        }).Throws<InvalidOperationException>();
        using var ok = Interpreter.Execute(program, s => s.MaxLoopIterations = 4);
        await Assert.That(ok.GetValue<long>()).IsEqualTo(3L);
    }

    [Test]
    public async Task MaxLoopIterations_NoDebug_DoesNotGuard() {
        var i = new Variable("i");
        var program = Interpreter.Compile(CountToThree(i), CompilationMode.NoDebug);
        using var exec = Interpreter.Execute(program, s => s.MaxLoopIterations = 1);
        await Assert.That(exec.GetValue<long>()).IsEqualTo(3L);
        await Assert.That(exec.State.LoopTicks).IsEqualTo(0L);
    }

    [Test]
    public async Task MaxLoopIterations_Unlimited_DoesNotIncrementLoopTicks() {
        var i = new Variable("i");
        var program = Interpreter.Compile(CountToThree(i));
        using var exec = Interpreter.Execute(program, s => s.MaxLoopIterations = -1);
        await Assert.That(exec.GetValue<long>()).IsEqualTo(3L);
        await Assert.That(exec.State.LoopTicks).IsEqualTo(0L);
    }

    [Test]
    public async Task MaxLoopIterations_Reset_ZerosLoopTicks() {
        var program = Interpreter.Compile(new WhileLoop(new Constant(true), new Constant(0L)));
        var state = new VmState(program) { MaxLoopIterations = 5, Registers = new long[256] };
        state.Status = InterpreterStatus.Running;
        try {
            try { program.Delegate(state); } catch (InvalidOperationException) { }
            await Assert.That(state.LoopTicks).IsGreaterThan(5L);
            state.Reset();
            await Assert.That(state.LoopTicks).IsEqualTo(0L);
        }
        finally {
            state.Dispose();
        }
    }

    [Test]
    public async Task CompileChecked_RootThisReference_DoesNotThrow() {
        var program = Interpreter.CompileChecked(new ThisReference(), new TypeDefinitionNodeAnalyzer());
        using var exec = Interpreter.Execute(program);
        await Assert.That(exec.RawValue).IsEqualTo(0L);
    }

    [Test]
    public async Task GetValue_FloatConstant_IsBitcast() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new Constant(1.5f)));
        await Assert.That(exec.GetValue<float>()).IsEqualTo(1.5f);
    }

    [Test]
    public async Task GetValue_BoolConstant_IsTrueFalse() {
        using var t = Interpreter.Execute(Interpreter.Compile(new Constant(true)));
        using var f = Interpreter.Execute(Interpreter.Compile(new Constant(false)));
        await Assert.That(t.GetValue<bool>()).IsTrue();
        await Assert.That(f.GetValue<bool>()).IsFalse();
    }

    [Test]
    public async Task Return_Void_ExitsWithoutValue() {
        var program = Interpreter.Compile(new Block([Return.Void]));
        using var exec = Interpreter.Execute(program);
        await Assert.That(exec.Result.IsVoid).IsTrue();
        await Assert.That(exec.Result.HasValue).IsFalse();
        await Assert.That(exec.GetValue<long>()).IsEqualTo(0L);
    }

    [Test]
    public async Task Return_VoidAfterAssignment_DoesNotLeaveValue() {
        var x = new Variable("x");
        var node = new Block([
            new Assignment(x, new Constant(42L)),
            Return.Void
        ], [x]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.Result.IsVoid).IsTrue();
    }

    [Test]
    public async Task Vm_VariableWithValue_AssignsOnDeclare() {
        var x = new Variable("x", new Constant(41L));
        var node = new Block(x, new Add(x, new Constant(1L)));
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(42L);
    }

    [Test]
    public async Task Vm_VariableWithValue_AssignmentDoesNotReapplyInitializer() {
        var x = new Variable("x", new Constant(1L));
        var node = new Block(x, new Assignment(x, new Constant(10L)), x);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(10L);
    }

    [Test]
    public async Task Heap_AllocateNull_ReturnsZero() {
        var heap = new Heap();
        await Assert.That(heap.Allocate(null)).IsEqualTo(0);
        await Assert.That(heap.Get(0)).IsNull();
    }

    [Test]
    public async Task Heap_SetHandleZero_Throws() {
        var heap = new Heap();
        await Assert.That(() => heap.Set(0, "x")).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Heap_SetNull_RecyclesOnce_DoubleFreeThrows() {
        var heap = new Heap();
        int h = heap.Allocate("a");
        heap.Set(h, null);
        await Assert.That(() => heap.Set(h, null)).Throws<InvalidOperationException>();
        await Assert.That(() => heap.UnsafeSet(h, "z")).Throws<InvalidOperationException>();
        int reused = heap.Allocate("b");
        int next = heap.Allocate("c");
        await Assert.That(reused).IsEqualTo(h);
        await Assert.That(next).IsNotEqualTo(h);
        await Assert.That(heap.Get(reused)).IsEqualTo("b");
        await Assert.That(heap.Get(next)).IsEqualTo("c");
    }

    [Test]
    public async Task Word_IsHandle_PositiveOnly() {
        await Assert.That(new Word(0).IsHandle).IsFalse();
        await Assert.That(new Word(1).IsHandle).IsTrue();
        await Assert.That(new Word(-1).IsHandle).IsFalse();
    }

    [Test]
    public async Task SetArgs_Double_StoresIeeeBits() {
        await AssertSlot0(1.5, BitConverter.DoubleToInt64Bits(1.5));
    }

    [Test]
    public async Task SetArgs_Float_StoresIeeeBitsViaDouble() {
        await AssertSlot0(1.5f, BitConverter.DoubleToInt64Bits(1.5f));
    }

    [Test]
    public async Task SetArgs_Char_StoresCodeUnit() {
        await AssertSlot0('A', 'A');
    }

    [Test]
    public async Task SetArgs_Bool_StoresZeroOrOne() {
        await AssertSlot0(true, 1L);
        await AssertSlot0(false, 0L);
    }

    [Test]
    public async Task SetArgs_Null_StoresAbiZero() {
        await AssertSlot0(null, 0L);
    }

    [Test]
    public async Task SetArgs_UInt_StoresAsLong() {
        await AssertSlot0(uint.MaxValue, uint.MaxValue);
    }

    [Test]
    public async Task SetArgs_Decimal_IsHeapHandle() {
        var program = Interpreter.Compile(new ThisReference());
        using var exec = Interpreter.Execute(program, s => s.SetArgs(1.5m));
        await Assert.That(exec.RawValue).IsGreaterThan(0L);
        await Assert.That(exec.State.Heap.Get((int)exec.RawValue)).IsEqualTo(1.5m);
    }

    [Test]
    public async Task VmForEach_NonEnumerable_Throws() {
        var item = new Variable("item");
        var node = new ForEachLoop(item, new Constant(new object()), new Constant(0L));
        var program = Interpreter.Compile(node);
        await Assert.That(() => {
            using var exec = Interpreter.Execute(program);
        }).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task StandardAnalyzer_PassNames_MatchInterpreterPipeline() {
        string[] expected = [
            TypeDefinitionNodeAnalyzer.Id,
            ThisReferenceContextAnalyzer.Id,
            TypeAndMemberResolver.Id,
            ScopeValidator.Id,
            SideEffectAnalyzer.Id,
            ConstantFoldingPass.Id,
            JumpTargetAnalyzer.Id,
            ControlFlowAnalysisPass.Id,
            ExceptionRegionAnalyzer.Id,
            DefiniteAssignmentAnalyzer.Id,
            ValueRepresentationAnalyzer.Id,
            SyntaxTypeCompatibilityAnalyzer.Id,
            CallSiteCatalogAnalyzer.Id,
            LambdaReturnTypeAnalyzer.Id,
        ];
        await Assert.That(string.Join(",", Interpreter.Analyzer.PassNames)).IsEqualTo(string.Join(",", expected));
    }

    private static Node CountToThree(Variable i) => new Block([
        new Assignment(i, new Constant(0L)),
        new WhileLoop(
            new LessThan(i, new Constant(3L)),
            new Assignment(i, new Add(i, new Constant(1L)))),
        i
    ], [i]);

    private static async Task AssertExceedsLoopLimit(Node node) {
        var program = Interpreter.Compile(node);
        await Assert.That(() => {
            using var exec = Interpreter.Execute(program, s => s.MaxLoopIterations = 10);
        }).Throws<InvalidOperationException>()
            .WithMessage("MaxLoopIterations exceeded.");
    }

    private static async Task AssertSlot0(object? arg, long expectedRaw) {
        var program = Interpreter.Compile(new ThisReference());
        using var exec = Interpreter.Execute(program, s => s.SetArgs(arg));
        await Assert.That(exec.RawValue).IsEqualTo(expectedRaw);
    }

    private static IEnumerable<int> Forever(int value) {
        while (true) yield return value;
    }

    private sealed class TrackingDisposable : IDisposable {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}