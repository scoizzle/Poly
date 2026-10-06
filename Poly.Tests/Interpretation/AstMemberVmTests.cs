using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm;
using Poly.Introspection;

namespace Poly.Tests.Interpretation;

/// <summary>F14: Member/Assignment on dict-backed AST property/field via Interpreter.Compile.</summary>
public class AstMemberVmTests {
    private static TypeDefinitionNodeAnalyzer BuildItemType(bool field = false) {
        TypeDefinitionNode typeNode = field
            ? new TypeDefinitionNode(
                "Item", "Sample",
                Fields: [new FieldDefinitionNode("Count", new PrimitiveTypeReference(PrimitiveType.Int64))])
            : new TypeDefinitionNode(
                "Item", "Sample",
                Properties: [new PropertyDefinitionNode("Count", new PrimitiveTypeReference(PrimitiveType.Int64))]);
        var tda = new TypeDefinitionNodeAnalyzer();
        tda.Analyze(AnalysisContext.CreateDefault(), typeNode);
        return tda;
    }

    [Test]
    public async Task CompileExecute_Member_AstProperty_ReadsDict() {
        var tda = BuildItemType();
        var bag = new Dictionary<string, object?> { ["Count"] = 42L };
        var entity = new Parameter("entity", new TypeReference("Sample.Item"));
        var program = Interpreter.Compile(new Member(entity, "Count"), tda);
        using var exec = Interpreter.Execute(program, s => s.SetArgs(new object?[] { bag }));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(42L);
    }

    [Test]
    public async Task CompileExecute_Assignment_AstProperty_WritesDict() {
        var tda = BuildItemType();
        var bag = new Dictionary<string, object?>();
        var entity = new Parameter("entity", new TypeReference("Sample.Item"));
        var node = new Assignment(new Member(entity, "Count"), new Constant(99L));
        var program = Interpreter.Compile(node, tda);
        using var exec = Interpreter.Execute(program, s => s.SetArgs(new object?[] { bag }));
        await Assert.That(bag["Count"]).IsEqualTo(99L);
    }

    [Test]
    public async Task CompileExecute_Member_AstField_ReadsDict() {
        var tda = BuildItemType(field: true);
        var bag = new Dictionary<string, object?> { ["Count"] = 3L };
        var entity = new Parameter("entity", new TypeReference("Sample.Item"));
        var program = Interpreter.Compile(new Member(entity, "Count"), tda);
        using var exec = Interpreter.Execute(program, s => s.SetArgs(new object?[] { bag }));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(3L);
    }

    [Test]
    public async Task CompileExecute_New_AstConstructor_WritesDict() {
        var count = new Parameter("count", new PrimitiveTypeReference(PrimitiveType.Int64));
        var typeNode = new TypeDefinitionNode(
            "Item", "Sample",
            Constructors: [
                new ConstructorDefinitionNode(
                    [count],
                    new Block([
                        new Assignment(new Member(new ThisReference(), "Count"), count)
                    ]))
            ],
            Properties: [new PropertyDefinitionNode("Count", new PrimitiveTypeReference(PrimitiveType.Int64))]);
        var tda = new TypeDefinitionNodeAnalyzer();
        tda.Analyze(AnalysisContext.CreateDefault(), typeNode);
        var program = Interpreter.Compile(new New(new TypeReference("Sample.Item"), new Constant(7L)), tda);
        using var exec = Interpreter.Execute(program);
        var bag = exec.GetValue<Dictionary<string, object?>>();
        await Assert.That(bag).IsNotNull();
        await Assert.That(bag!["Count"]).IsEqualTo(7L);
    }

    [Test]
    public async Task CompileExecute_New_AstConstructor_NestsDictionary() {
        var child = new Parameter("child", new TypeReference("Sample.Item"));
        var typeNode = new TypeDefinitionNode(
            "Item", "Sample",
            Constructors: [
                new ConstructorDefinitionNode(),
                new ConstructorDefinitionNode(
                    [child],
                    new Block([
                        new Assignment(new Member(new ThisReference(), "Child"), child)
                    ]))
            ],
            Properties: [new PropertyDefinitionNode("Child", new TypeReference("Sample.Item"))]);
        var tda = new TypeDefinitionNodeAnalyzer();
        tda.Analyze(AnalysisContext.CreateDefault(), typeNode);
        var typeRef = new TypeReference("Sample.Item");
        var program = Interpreter.Compile(new New(typeRef, new New(typeRef)), tda);
        using var exec = Interpreter.Execute(program);
        var bag = exec.GetValue<Dictionary<string, object?>>();
        await Assert.That(bag).IsNotNull();
        var nested = bag!["Child"] as Dictionary<string, object?>;
        await Assert.That(nested).IsNotNull();
        await Assert.That(nested!.Count).IsEqualTo(0);
    }

    [Test]
    public async Task CompileExecute_Invoke_AstMethod_MutatesDict() {
        var typeNode = new TypeDefinitionNode(
            "Item", "Sample",
            Properties: [new PropertyDefinitionNode("Count", new PrimitiveTypeReference(PrimitiveType.Int64))],
            Methods: [
                new MethodDefinitionNode(
                    "Inc",
                    new PrimitiveTypeReference(PrimitiveType.Int64),
                    Body: new Assignment(
                        new Member(new ThisReference(), "Count"),
                        new Add(new Member(new ThisReference(), "Count"), new Constant(1L))))
            ]);
        var tda = new TypeDefinitionNodeAnalyzer();
        tda.Analyze(AnalysisContext.CreateDefault(), typeNode);
        var bag = new Dictionary<string, object?> { ["Count"] = 7L };
        var entity = new Parameter("entity", new TypeReference("Sample.Item"));
        var program = Interpreter.Compile(new Invoke(new Member(entity, "Inc")), tda);
        using var exec = Interpreter.Execute(program, s => s.SetArgs(new object?[] { bag }));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(8L);
        await Assert.That(bag["Count"]).IsEqualTo(8L);
    }

    private static TypeDefinitionNodeAnalyzer Analyze(TypeDefinitionNode typeNode) {
        var tda = new TypeDefinitionNodeAnalyzer();
        tda.Analyze(AnalysisContext.CreateDefault(), typeNode);
        return tda;
    }

    private static readonly Parameter ObjectValue = new("v", TypeReference.To<object>());

    // Box(object v) { this.V = v; } and Set(object v) { this.V = v; }
    private static TypeDefinitionNode BoxType() => new(
        "Box", "Sample",
        Constructors: [
            new ConstructorDefinitionNode(
                [ObjectValue],
                new Block([new Assignment(new Member(new ThisReference(), "V"), ObjectValue)]))
        ],
        Properties: [new PropertyDefinitionNode("V", TypeReference.To<object>())],
        Methods: [
            new MethodDefinitionNode(
                "Set",
                new ClrTypeReference(typeof(void)),
                Parameters: [ObjectValue],
                Body: new Block([new Assignment(new Member(new ThisReference(), "V"), ObjectValue)]))
        ]);

    [Test]
    public async Task CompileExecute_New_AstConstructor_ScalarToObjectParam_KeepsValue() {
        var tda = Analyze(BoxType());
        var program = Interpreter.Compile(new New(new TypeReference("Sample.Box"), new Constant(42L)), tda);
        using var exec = Interpreter.Execute(program);
        var bag = exec.GetValue<Dictionary<string, object?>>();
        await Assert.That(bag!["V"]).IsEqualTo(42L);
    }

    [Test]
    public async Task CompileExecute_Invoke_AstMethod_ScalarToObjectParam_KeepsValue() {
        var tda = Analyze(BoxType());
        var bag = new Dictionary<string, object?>();
        var box = new Parameter("box", new TypeReference("Sample.Box"));
        var program = Interpreter.Compile(new Invoke(new Member(box, "Set"), new Constant(42L)), tda);
        using var exec = Interpreter.Execute(program, s => s.SetArgs(new object?[] { bag }));
        await Assert.That(bag["V"]).IsEqualTo(42L);
    }

    [Test]
    public async Task CompileExecute_Invoke_AstMethod_TextConcatToObjectParam_KeepsText() {
        var tda = Analyze(BoxType());
        var bag = new Dictionary<string, object?>();
        var box = new Parameter("box", new TypeReference("Sample.Box"));
        var program = Interpreter.Compile(
            new Invoke(new Member(box, "Set"), new Add(new Constant("a"), new Constant("b"))), tda);
        using var exec = Interpreter.Execute(program, s => s.SetArgs(new object?[] { bag }));
        await Assert.That(bag["V"]).IsEqualTo("ab");
    }

    [Test]
    public async Task Compile_SameAstMethod_InEachMode_KeepsThatModesLoopGuard() {
        // Spin() { while (this.Count < 50) this.Count = this.Count + 1; }
        var count = new Member(new ThisReference(), "Count");
        var tda = Analyze(new TypeDefinitionNode(
            "Spinner", "Sample",
            Properties: [new PropertyDefinitionNode("Count", new PrimitiveTypeReference(PrimitiveType.Int64))],
            Methods: [
                new MethodDefinitionNode(
                    "Spin",
                    new ClrTypeReference(typeof(void)),
                    Body: new WhileLoop(
                        new LessThan(count, new Constant(50L)),
                        new Assignment(count, new Add(count, new Constant(1L)))))
            ]));
        var spinner = new Parameter("spinner", new TypeReference("Sample.Spinner"));
        var call = new Invoke(new Member(spinner, "Spin"));

        // The first compile has no loop guard; the second must not reuse that body.
        Interpreter.Compile(call, tda, CompilationMode.NoDebug);
        var guarded = Interpreter.Compile(call, tda, CompilationMode.Normal);
        var bag = new Dictionary<string, object?> { ["Count"] = 0L };

        await Assert.That(() => {
            using var exec = Interpreter.Execute(guarded, s => {
                s.MaxLoopIterations = 10;
                s.SetArgs(new object?[] { bag });
            });
        }).Throws<InvalidOperationException>().WithMessageContaining("MaxLoopIterations");
    }

    // A method body with a lambda does not compile as its own frame.
    private static Node LambdaArgument() =>
        new Lambda([new Parameter("x", TypeReference.To<long>())], new Parameter("x"));

    [Test]
    public async Task Compile_AstMethodNamedCreate_BodyFails_FallsBack() {
        var tda = Analyze(new TypeDefinitionNode(
            "Shop", "Sample",
            Methods: [
                new MethodDefinitionNode(
                    "Create",
                    new ClrTypeReference(typeof(void)),
                    Body: new Block([new Variable("f"), new Assignment(new Variable("f"), LambdaArgument())]))
            ]));
        var shop = new Parameter("shop", new TypeReference("Sample.Shop"));

        await Assert.That(() => Interpreter.Compile(new Invoke(new Member(shop, "Create")), tda))
            .ThrowsNothing();
    }

    [Test]
    public async Task Compile_ClrForwarder_BodyFails_Throws() {
        // Run() { HostJobProbe.Take(this, x => x); }
        var tda = Analyze(new TypeDefinitionNode(
            "Shop", "Sample",
            Methods: [
                new MethodDefinitionNode(
                    "Run",
                    new ClrTypeReference(typeof(void)),
                    Body: new Block([new Invoke(
                        new Member(new ClrTypeReference(typeof(HostJobProbe)), nameof(HostJobProbe.Take)),
                        new ThisReference(), LambdaArgument())]))
            ]));
        var shop = new Parameter("shop", new TypeReference("Sample.Shop"));

        await Assert.That(() => Interpreter.Compile(new Invoke(new Member(shop, "Run")), tda))
            .Throws<InvalidOperationException>().WithMessageContaining("AST host job 'Run'");
    }

    public static class HostJobProbe {
        public static void Take(object? instance, Func<long, long> f) { }
    }
}
