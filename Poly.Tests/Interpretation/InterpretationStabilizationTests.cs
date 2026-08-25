using Poly.Interpretation;
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

    private sealed class TrackingDisposable : IDisposable {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}