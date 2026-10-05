using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
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
}