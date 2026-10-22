using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Intra-emitter branches. <see cref="LanguageVmTests"/> keeps one oracle per
/// executable kind; this file runs the other side of each emitter, including
/// the nested frame <c>CompileFunctionBody</c> reserves for parameters.
/// </summary>
public class InterpreterBranchVmTests {
    [Test]
    public async Task InstanceMethod_BlockLocals_DoNotWipeParameter() {
        var plate = new Parameter("plate", TypeReference.To<string>());
        var tda = Analyze(new TypeDefinitionNode(
            "ReadLot", "Branch",
            Methods: [
                new MethodDefinitionNode(
                    "Read",
                    TypeReference.To<string>(),
                    Parameters: [plate],
                    Body: WipeThen(new Return(plate)))
            ]));
        var entity = new Parameter("entity", new TypeReference("Branch.ReadLot"));
        var program = Interpreter.Compile(
            new Invoke(new Member(entity, "Read"), new Constant("KEEP")), tda);
        using var exec = Interpreter.Execute(program, s => s.SetArgs(new Dictionary<string, object?>()));
        await Assert.That(exec.GetValue<string>()).IsEqualTo("KEEP");
    }

    [Test]
    public async Task StaticMethod_BlockLocals_DoNotWipeParameter() {
        var plate = new Parameter("plate", TypeReference.To<string>());
        var tda = Analyze(new TypeDefinitionNode(
            "StaticLot", "Branch",
            Methods: [
                new MethodDefinitionNode(
                    "Read",
                    TypeReference.To<string>(),
                    Parameters: [plate],
                    Body: WipeThen(new Return(plate)),
                    IsStatic: true)
            ]));
        var program = Interpreter.Compile(
            new Invoke(new Member(new TypeReference("Branch.StaticLot"), "Read"), new Constant("KEEP")),
            tda);
        using var exec = Interpreter.Execute(program);
        await Assert.That(exec.GetValue<string>()).IsEqualTo("KEEP");
    }

    [Test]
    public async Task InstanceMethod_AfterStaticCall_StillReadsParameter() {
        var inner = new Parameter("plate", TypeReference.To<string>());
        var outer = new Parameter("plate", TypeReference.To<string>());
        var tda = Analyze(new TypeDefinitionNode(
            "RelayLot", "Branch",
            Properties: [
                new PropertyDefinitionNode("Seen", TypeReference.To<string>()),
                new PropertyDefinitionNode("Plate", TypeReference.To<string>())
            ],
            Methods: [
                new MethodDefinitionNode(
                    "Echo",
                    TypeReference.To<string>(),
                    Parameters: [inner],
                    Body: WipeThen(new Return(inner)),
                    IsStatic: true),
                new MethodDefinitionNode(
                    "Wrap",
                    TypeReference.To<string>(),
                    Parameters: [outer],
                    Body: WipeThen(new Block([
                        new Assignment(
                            new Member(new ThisReference(), "Seen"),
                            new Invoke(new Member(new TypeReference("Branch.RelayLot"), "Echo"), outer)),
                        new Assignment(new Member(new ThisReference(), "Plate"), outer),
                        new Return(outer)
                    ])))
            ]));
        var bag = new Dictionary<string, object?>();
        var entity = new Parameter("entity", new TypeReference("Branch.RelayLot"));
        var program = Interpreter.Compile(
            new Invoke(new Member(entity, "Wrap"), new Constant("KEEP")), tda);
        using var exec = Interpreter.Execute(program, s => s.SetArgs(bag));
        await Assert.That(exec.GetValue<string>()).IsEqualTo("KEEP");
        await Assert.That(bag["Seen"]).IsEqualTo("KEEP");
        await Assert.That(bag["Plate"]).IsEqualTo("KEEP");
    }

    [Test]
    public async Task InstanceMethod_AfterInstanceCall_StillReadsParameter() {
        var inner = new Parameter("plate", TypeReference.To<string>());
        var outer = new Parameter("plate", TypeReference.To<string>());
        var tda = Analyze(new TypeDefinitionNode(
            "StampLot", "Branch",
            Properties: [new PropertyDefinitionNode("Plate", TypeReference.To<string>())],
            Methods: [
                new MethodDefinitionNode(
                    "Stamp",
                    TypeReference.To<string>(),
                    Parameters: [inner],
                    Body: WipeThen(new Return(inner))),
                new MethodDefinitionNode(
                    "Relay",
                    TypeReference.To<string>(),
                    Parameters: [outer],
                    Body: WipeThen(new Block([
                        new Assignment(
                            new Member(new ThisReference(), "Plate"),
                            new Invoke(new Member(new ThisReference(), "Stamp"), outer)),
                        new Return(outer)
                    ])))
            ]));
        var bag = new Dictionary<string, object?>();
        var entity = new Parameter("entity", new TypeReference("Branch.StampLot"));
        var program = Interpreter.Compile(
            new Invoke(new Member(entity, "Relay"), new Constant("KEEP")), tda);
        using var exec = Interpreter.Execute(program, s => s.SetArgs(bag));
        await Assert.That(exec.GetValue<string>()).IsEqualTo("KEEP");
        await Assert.That(bag["Plate"]).IsEqualTo("KEEP");
    }

    [Test]
    public async Task InstanceMethod_BlockLocals_DoNotWipeReceiver() {
        var tda = Analyze(new TypeDefinitionNode(
            "NamedLot", "Branch",
            Properties: [new PropertyDefinitionNode("Name", TypeReference.To<string>())],
            Methods: [
                new MethodDefinitionNode(
                    "Read",
                    TypeReference.To<string>(),
                    Body: WipeThen(new Return(new Member(new ThisReference(), "Name"))))
            ]));
        var bag = new Dictionary<string, object?> { ["Name"] = "LOT" };
        var entity = new Parameter("entity", new TypeReference("Branch.NamedLot"));
        var program = Interpreter.Compile(new Invoke(new Member(entity, "Read")), tda);
        using var exec = Interpreter.Execute(program, s => s.SetArgs(bag));
        await Assert.That(exec.GetValue<string>()).IsEqualTo("LOT");
    }

    [Test]
    public async Task Constructor_BlockLocals_StoreParameterOnDictionary() {
        var plate = new Parameter("plate", TypeReference.To<string>());
        var a = new Variable("a");
        var b = new Variable("b");
        var tda = Analyze(new TypeDefinitionNode(
            "BuiltLot", "Branch",
            Constructors: [
                new ConstructorDefinitionNode(
                    [plate],
                    new Block([
                        new Assignment(a, new Constant("wipe-a")),
                        new Assignment(b, new Constant("wipe-b")),
                        new Assignment(new Member(new ThisReference(), "Plate"), plate)
                    ], [a, b]))
            ],
            Properties: [new PropertyDefinitionNode("Plate", TypeReference.To<string>())]));
        var program = Interpreter.Compile(
            new New(new TypeReference("Branch.BuiltLot"), new Constant("KEEP")), tda);
        using var exec = Interpreter.Execute(program);
        var bag = exec.GetValue<Dictionary<string, object?>>();
        await Assert.That(bag).IsNotNull();
        await Assert.That(bag!["Plate"]).IsEqualTo("KEEP");
    }

    [Test]
    public async Task Constructor_ReturnValue_IsNotTheNewResult() {
        var tda = Analyze(new TypeDefinitionNode(
            "ReturnedLot", "Branch",
            Constructors: [
                new ConstructorDefinitionNode(
                    Body: new Block([new Return(new Constant("nope"))]))
            ]));
        var program = Interpreter.Compile(new New(new TypeReference("Branch.ReturnedLot")), tda);
        using var exec = Interpreter.Execute(program);
        var bag = exec.GetValue<Dictionary<string, object?>>();
        await Assert.That(bag).IsNotNull();
        await Assert.That(bag!.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Constructor_EmptyBody_ReturnsDictionaryWithoutRunningBody() {
        var tda = Analyze(new TypeDefinitionNode(
            "EmptyLot", "Branch",
            Constructors: [new ConstructorDefinitionNode(Body: new Block([]))]));
        var program = Interpreter.Compile(new New(new TypeReference("Branch.EmptyLot")), tda);
        using var exec = Interpreter.Execute(program);
        var bag = exec.GetValue<Dictionary<string, object?>>();
        await Assert.That(bag).IsNotNull();
        await Assert.That(bag!.Count).IsEqualTo(0);
    }

    [Test]
    public async Task AstMethod_OmittedTrailingDefaults_UseDefaults() {
        var (tda, call) = Pick(arguments: 0);
        using var exec = Interpreter.Execute(Interpreter.Compile(call, tda));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(8L);
    }

    [Test]
    public async Task AstMethod_OneExplicitArgument_FillsOnlyTheTrailingDefault() {
        var (tda, call) = Pick(arguments: 1);
        using var exec = Interpreter.Execute(Interpreter.Compile(call, tda));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(12L);
    }

    [Test]
    public async Task AstMethod_ExplicitArgument_OverridesDefault() {
        var (tda, call) = Pick(arguments: 2);
        using var exec = Interpreter.Execute(Interpreter.Compile(call, tda));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(11L);
    }

    [Test]
    public async Task AstMethod_OmittedStringDefault_UsesHeapDefault() {
        var extra = new Parameter("extra", TypeReference.To<string>(), DefaultValue: new Constant("DEF"));
        var tda = Analyze(new TypeDefinitionNode(
            "TextLot", "Branch",
            Methods: [
                new MethodDefinitionNode(
                    "Pick",
                    TypeReference.To<string>(),
                    Parameters: [
                        new Parameter("written", TypeReference.To<string>()),
                        extra
                    ],
                    Body: new Return(extra),
                    IsStatic: true)
            ]));
        var program = Interpreter.Compile(
            new Invoke(new Member(new TypeReference("Branch.TextLot"), "Pick"), new Constant("KEEP")),
            tda);
        using var exec = Interpreter.Execute(program);
        await Assert.That(exec.GetValue<string>()).IsEqualTo("DEF");
    }

    [Test]
    public async Task AstMethod_NonConstantDefault_OmittedArgumentIsZero() {
        var extra = new Parameter(
            "extra",
            new PrimitiveTypeReference(PrimitiveType.Int64),
            DefaultValue: new Add(new Constant(1L), new Constant(1L)));
        var tda = Analyze(new TypeDefinitionNode(
            "OpenLot", "Branch",
            Methods: [
                new MethodDefinitionNode(
                    "Pick",
                    new PrimitiveTypeReference(PrimitiveType.Int64),
                    Parameters: [
                        new Parameter("written", new PrimitiveTypeReference(PrimitiveType.Int64)),
                        extra
                    ],
                    Body: new Return(extra),
                    IsStatic: true)
            ]));
        var program = Interpreter.Compile(
            new Invoke(new Member(new TypeReference("Branch.OpenLot"), "Pick"), new Constant(4L)),
            tda);
        using var exec = Interpreter.Execute(program);
        await Assert.That(exec.GetValue<long>()).IsEqualTo(0L);
    }

    [Test]
    public async Task AstMethod_TooManyArguments_CompileRejected() {
        var (tda, call) = Pick(arguments: 3);
        await Assert.That(() => Interpreter.Compile(call, tda))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("argument");
    }

    [Test]
    public async Task InstanceMethod_OnTypeName_CompileRejected() {
        var plate = new Parameter("plate", TypeReference.To<string>());
        var tda = Analyze(new TypeDefinitionNode(
            "InstanceLot", "Branch",
            Methods: [
                new MethodDefinitionNode(
                    "Read",
                    TypeReference.To<string>(),
                    Parameters: [plate],
                    Body: new Return(plate))
            ]));
        await Assert.That(() => Interpreter.Compile(
                new Invoke(new Member(new TypeReference("Branch.InstanceLot"), "Read"), new Constant("KEEP")),
                tda))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("type name");
    }

    [Test]
    public async Task Not_True_IsFalse() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new Not(new Constant(true))));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(0L);
    }

    [Test]
    public async Task ForEach_NonEnumerable_Throws() {
        var item = new Variable("item");
        var program = Interpreter.Compile(
            new ForEachLoop(item, new Constant(new object()), new Constant(0L)));
        await Assert.That(() => {
            using var exec = Interpreter.Execute(program);
        }).Throws<InvalidOperationException>()
            .WithMessageContaining("not IEnumerable");
    }

    [Test]
    public async Task ForLoop_NullInitializer_ContinueStillIncrements() {
        var i = new Variable("i");
        var n = new Variable("n");
        var node = new Block([
            new Assignment(i, new Constant(0L)),
            new Assignment(n, new Constant(0L)),
            new ForLoop(
                null,
                new LessThan(i, new Constant(3L)),
                new Assignment(i, new Add(i, new Constant(1L))),
                new Block([
                    new IfStatement(new Equal(i, new Constant(1L)), new ContinueStatement()),
                    new Assignment(n, new Add(n, new Constant(1L)))
                ])),
            n
        ], [i, n]);
        var program = Interpreter.Compile(node);
        using var exec = Interpreter.Execute(program, s => s.MaxLoopIterations = 20);
        await Assert.That(exec.GetValue<long>()).IsEqualTo(2L);
    }

    [Test]
    public async Task ForLoop_NullConditionAndIncrement_BreakStops() {
        var n = new Variable("n");
        var node = new Block([
            new Assignment(n, new Constant(0L)),
            new ForLoop(
                null,
                null,
                null,
                new Block([
                    new Assignment(n, new Add(n, new Constant(1L))),
                    new BreakStatement()
                ])),
            n
        ], [n]);
        var program = Interpreter.Compile(node);
        using var exec = Interpreter.Execute(program, s => s.MaxLoopIterations = 5);
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task NewArray_LengthOne_StoresAndLoads() {
        using var exec = Interpreter.Execute(Interpreter.Compile(StoreLoad(1L, 0L, 9L)));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(9L);
    }

    [Test]
    public async Task NewArray_LengthZero_IndexThrows() {
        var arr = new Variable("arr");
        var program = Interpreter.Compile(new Block([
            new Assignment(arr, new NewArray(TypeReference.To<long>(), new Constant(0L))),
            new IndexAccess(arr, new Constant(0L))
        ], [arr]));
        await Assert.That(() => {
            using var exec = Interpreter.Execute(program);
        }).Throws<IndexOutOfRangeException>();
    }

    [Test]
    public async Task NewArray_PastSmallThreshold_StoresLastElement() {
        using var exec = Interpreter.Execute(Interpreter.Compile(StoreLoad(17L, 16L, 9L)));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(9L);
    }

    [Test]
    public async Task StridedSetBits_HeapArray_SetsWordBits() {
        var arr = new Variable("arr");
        var node = new Block([
            new Assignment(arr, new NewArray(TypeReference.To<long>(), new Constant(17L))),
            new StridedSetBits(arr, new Constant(4L), new Constant(2L), new Constant(8L)),
            new IndexAccess(arr, new Constant(0L))
        ], [arr]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(336L);
    }

    [Test]
    public async Task Return_WithoutValue_SkipsLaterCall() {
        var probe = new MarkProbe();
        var program = Interpreter.Compile(new Block([
            new Return(),
            new Invoke(new Member(new Constant(probe), "Mark"))
        ]));
        using var exec = Interpreter.Execute(program);
        await Assert.That(exec.Result.IsVoid).IsTrue();
        await Assert.That(probe.Hits).IsEqualTo(0);
    }

    [Test]
    public async Task Lambda_TooManyArguments_CompileRejected() {
        var x = new Parameter("x", TypeReference.To<long>());
        await Assert.That(() => Interpreter.Compile(
                new Invoke(new Lambda([x], x), new Constant(1L), new Constant(2L))))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("argument");
    }

    [Test]
    public async Task Invoke_ConstantTarget_CompileRejected() {
        await Assert.That(() => Interpreter.Compile(new Invoke(new Constant(1L))))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("Invoke target");
    }

    [Test]
    public async Task IndexAccess_WithoutIndex_ReadsFirstElement() {
        var arr = new Variable("arr");
        var node = new Block([
            new Assignment(arr, new NewArray(TypeReference.To<long>(), new Constant(1L))),
            new Assignment(new IndexAccess(arr), new Constant(4L)),
            new IndexAccess(arr)
        ], [arr]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(4L);
    }

    [Test]
    public async Task Default_Guid_IsEmptyStruct() {
        using var exec = Interpreter.Execute(
            Interpreter.Compile(new Default(TypeReference.To<Guid>())));
        await Assert.That(exec.GetValue<Guid>()).IsEqualTo(Guid.Empty);
    }

    [Test]
    public async Task Using_NonDisposable_RunsBody() {
        var probe = new MarkProbe();
        var program = Interpreter.Compile(new UsingStatement(
            new Constant("text"),
            new Invoke(new Member(new Constant(probe), "Mark"))));
        using var exec = Interpreter.Execute(program);
        await Assert.That(probe.Hits).IsEqualTo(1);
    }

    [Test]
    public async Task Try_WithoutCatchOrFinally_RunsBody() {
        using var exec = Interpreter.Execute(
            Interpreter.Compile(new TryCatchFinally(new Constant(4L))));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(4L);
    }

    private static ITypeDefinitionProvider Analyze(TypeDefinitionNode typeNode) {
        var tda = new TypeDefinitionNodeAnalyzer();
        var ctx = AnalysisContext.CreateDefault();
        tda.Analyze(ctx, typeNode);
        return ctx.TypeDefinitions;
    }

    private static Block WipeThen(Node tail) {
        var a = new Variable("a");
        var b = new Variable("b");
        return new Block([
            new Assignment(a, new Constant("wipe-a")),
            new Assignment(b, new Constant("wipe-b")),
            tail
        ], [a, b]);
    }

    /// <summary>
    /// static long Pick(long written = 1, long extra = 7).
    /// Zero arguments yield 1 + 7. One argument, 5, yields 5 + 7.
    /// Two arguments yield 4 + 7. A third argument is rejected.
    /// </summary>
    private static (ITypeDefinitionProvider Types, Invoke Call) Pick(int arguments) {
        var written = new Parameter(
            "written",
            new PrimitiveTypeReference(PrimitiveType.Int64),
            DefaultValue: new Constant(1L));
        var extra = new Parameter(
            "extra",
            new PrimitiveTypeReference(PrimitiveType.Int64),
            DefaultValue: new Constant(7L));
        var tda = Analyze(new TypeDefinitionNode(
            "PickLot", "Branch",
            Methods: [
                new MethodDefinitionNode(
                    "Pick",
                    new PrimitiveTypeReference(PrimitiveType.Int64),
                    Parameters: [written, extra],
                    Body: new Return(new Add(written, extra)),
                    IsStatic: true)
            ]));
        Node[] args = arguments switch {
            0 => [],
            1 => [new Constant(5L)],
            2 => [new Constant(4L), new Constant(7L)],
            _ => [new Constant(4L), new Constant(7L), new Constant(9L)]
        };
        return (tda, new Invoke(new Member(new TypeReference("Branch.PickLot"), "Pick"), args));
    }

    private static Block StoreLoad(long length, long index, long value) {
        var arr = new Variable("arr");
        return new Block([
            new Assignment(arr, new NewArray(TypeReference.To<long>(), new Constant(length))),
            new Assignment(new IndexAccess(arr, new Constant(index)), new Constant(value)),
            new IndexAccess(arr, new Constant(index))
        ], [arr]);
    }

    private sealed class MarkProbe {
        public int Hits { get; private set; }
        public void Mark() => Hits++;
    }
}