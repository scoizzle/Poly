using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;

namespace Poly.Tests.Interpretation;

/// <summary>Reader-shaped type references (<c>System.Int64</c>, <c>System.DateTime</c>)
/// resolve in the type pass; VM emit consumes those resolved types without peeking at
/// the type-reference node kind.</summary>
public class ResolvedTypeVmTests {
    [Test]
    public async Task Default_QualifiedInt64_IsZero() {
        using var exec = Interpreter.Execute(Interpreter.Compile(
            new Default(new TypeReference("System.Int64"))));

        await Assert.That(exec.GetValue<long>()).IsEqualTo(0L);
    }

    [Test]
    public async Task New_QualifiedDateTime_RunsConstructor() {
        var node = new New(
            new TypeReference("System.DateTime"),
            new Constant(2020), new Constant(1), new Constant(2));

        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        await Assert.That(exec.GetValue<DateTime>()).IsEqualTo(new DateTime(2020, 1, 2));
    }

    [Test]
    public async Task NewArray_QualifiedInt64_RoundTrips() {
        var arr = new Variable("arr");
        var body = new Block(
            [
                new Assignment(arr, new NewArray(new TypeReference("System.Int64"), new Constant(3L))),
                new Assignment(new IndexAccess(arr, new Constant(1L)), new Constant(7L)),
                new IndexAccess(arr, new Constant(1L))
            ],
            [arr]);

        using var exec = Interpreter.Execute(Interpreter.Compile(body));

        await Assert.That(exec.GetValue<long>()).IsEqualTo(7L);
    }

    [Test]
    public async Task Analyze_ProviderTypes_ShareTheCompilationUnit() {
        // The provider's type declarations are part of the analyzed unit, so a
        // member body carries resolved facts before emit runs.
        var cast = new TypeCast(new Constant(42L), new ClrTypeReference(typeof(long)));
        var typeNode = new TypeDefinitionNode(
            "Caster", "Sample",
            Methods: [
                new MethodDefinitionNode(
                    "Cast",
                    new PrimitiveTypeReference(PrimitiveType.Int64),
                    Parameters: [],
                    Body: cast,
                    IsStatic: true)
            ]);
        var tda = Analyze(typeNode);

        var analysis = Interpreter.Analyze(
            new Invoke(new Member(new TypeReference("Sample.Caster"), "Cast")), tda);

        await Assert.That(analysis.GetResolvedType(cast)).IsNotNull();
    }

    [Test]
    public async Task AstBody_NoVariables_ExecutesFromTheUnitAnalysis() {
        // A body with no Variable/Member nodes still executes from the shared
        // unit analysis: its type nodes were walked with the provider's types.
        var typeNode = new TypeDefinitionNode(
            "Caster", "Sample",
            Methods: [
                new MethodDefinitionNode(
                    "Cast",
                    new PrimitiveTypeReference(PrimitiveType.Int64),
                    Parameters: [],
                    Body: new TypeCast(new Constant(42L), new ClrTypeReference(typeof(long))),
                    IsStatic: true)
            ]);
        var tda = Analyze(typeNode);
        var program = Interpreter.Compile(
            new Invoke(new Member(new TypeReference("Sample.Caster"), "Cast")), tda);

        using var exec = Interpreter.Execute(program);

        await Assert.That(exec.GetValue<long>()).IsEqualTo(42L);
    }

    [Test]
    public async Task Constructor_NullableParameter_KeepsScalar() {
        var value = new Parameter("value", new ClrTypeReference(typeof(int?)));
        var typeNode = new TypeDefinitionNode(
            "Box", "Sample",
            Constructors: [
                new ConstructorDefinitionNode(
                    [value],
                    new Block([
                        new Assignment(new Member(new ThisReference(), "Value"), value)
                    ]))
            ],
            Properties: [new PropertyDefinitionNode("Value", new ClrTypeReference(typeof(int?)))]);
        var tda = Analyze(typeNode);
        var program = Interpreter.Compile(new New(new TypeReference("Sample.Box"), new Constant(42)), tda);

        using var exec = Interpreter.Execute(program);
        var bag = exec.GetValue<Dictionary<string, object?>>();

        await Assert.That(bag!["Value"]).IsEqualTo(42);
    }

    [Test]
    public async Task Constructor_OmittedObjectDefault_StoresTheDefault() {
        var value = new Parameter("value", new ClrTypeReference(typeof(object)), new Constant(5L));
        var typeNode = new TypeDefinitionNode(
            "Holder", "Sample",
            Constructors: [
                new ConstructorDefinitionNode(
                    [value],
                    new Block([
                        new Assignment(new Member(new ThisReference(), "Value"), value)
                    ]))
            ],
            Properties: [new PropertyDefinitionNode("Value", new ClrTypeReference(typeof(object)))]);
        var tda = Analyze(typeNode);
        var program = Interpreter.Compile(new New(new TypeReference("Sample.Holder")), tda);

        using var exec = Interpreter.Execute(program);
        var bag = exec.GetValue<Dictionary<string, object?>>();

        await Assert.That(bag!["Value"]).IsEqualTo(5L);
    }

    [Test]
    public async Task New_QualifiedArgumentException_ReadsProperty() {
        var node = new Member(
            new New(new TypeReference("System.ArgumentException"),
                new Constant("bad"), new Constant("p")),
            "ParamName");

        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        await Assert.That(exec.GetValue<string>()).IsEqualTo("p");
    }

    private static ITypeDefinitionProvider Analyze(TypeDefinitionNode typeNode) {
        var tda = new TypeDefinitionNodeAnalyzer();
        var ctx = AnalysisContext.CreateDefault();
        tda.Analyze(ctx, typeNode);
        return ctx.TypeDefinitions;
    }
}