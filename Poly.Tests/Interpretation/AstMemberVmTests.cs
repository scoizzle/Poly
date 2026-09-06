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
}
