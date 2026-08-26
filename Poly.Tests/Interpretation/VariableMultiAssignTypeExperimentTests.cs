using Poly.Analysis;
using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.CSharp;
using Poly.Introspection;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Residual experiment notes for
/// <c>docs/experiments/variable-assignment-union-types.md</c>.
/// Mixed assigns to one <see cref="Variable"/> fail closed at analysis
/// (<see cref="InvalidProgramTests"/>). This file keeps same-kind reassign,
/// C# print of an illegal tree, and property-level <see cref="UnionTypeReference"/> collapse.
/// </summary>
public class VariableMultiAssignTypeExperimentTests {
    [Test]
    public async Task SequentialLongThenLong_StillRuns() {
        var x = new Variable("x");
        var node = new Block([
            new Assignment(x, new Constant(1L)),
            new Assignment(x, new Constant(2L)),
            x
        ], [x]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(2L);
    }

    [Test]
    public async Task SequentialIntThenLong_SameSlotEncoding_StillRuns() {
        var x = new Variable("x");
        var node = new Block([
            new Assignment(x, new Constant(1)),
            new Assignment(x, new Constant(2L)),
            x
        ], [x]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(2L);
    }

    [Test]
    public async Task CSharpPrint_SequentialLongThenString_FusesFirstAssign() {
        var x = new Variable("x");
        var node = new Block([
            new Assignment(x, new Constant(1L)),
            new Assignment(x, new Constant("hi")),
            x
        ], [x]);
        var cs = new CSharpGenerator().Generate(node);
        await Assert.That(cs).Contains("var x = 1L;");
        await Assert.That(cs).Contains("x = \"hi\";");
    }

    [Test]
    public async Task ExistingUnionTypeReference_MixedOptions_CollapseToObject() {
        var union = new UnionTypeReference([
            new PrimitiveTypeReference(PrimitiveType.Int64),
            new PrimitiveTypeReference(PrimitiveType.String)
        ]);
        var prop = new PropertyDefinitionNode("Value", union);
        var typeDef = new TypeDefinitionNode("Holder", Properties: [prop]);
        var analysis = new AnalyzerBuilder()
            .AddAnalyzer(new TypeDefinitionNodeAnalyzer())
            .Build()
            .Analyze(typeDef);
        var td = analysis.GetMetadata<TypeDefinitionMetadata>(typeDef)?.TypeDefinition;
        await Assert.That(td).IsNotNull();
        await Assert.That(td!.Properties.Single().MemberTypeDefinition.GetRuntimeType())
            .IsEqualTo(typeof(object));
    }
}