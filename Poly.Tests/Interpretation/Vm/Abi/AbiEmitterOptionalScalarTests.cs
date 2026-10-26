using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;

namespace Poly.Tests.Interpretation.Vm.Abi;

/// <summary>
/// Construction-side complement to <see cref="AbiEmitterNullScalarTests"/> (#330):
/// an optional scalar parameter is a heap handle (Nullable&lt;T&gt;) at a call frame,
/// and a required scalar cannot be omitted.
/// </summary>
public class AbiEmitterOptionalScalarTests {
    private static ITypeDefinitionProvider Analyze(TypeDefinitionNode typeNode) {
        var analyzer = new TypeDefinitionNodeAnalyzer();
        var context = AnalysisContext.CreateDefault();
        analyzer.Analyze(context, typeNode);
        return context.TypeDefinitions;
    }

    private static ITypeDefinitionProvider ProbeType(Parameter age, Node body) =>
        Analyze(new TypeDefinitionNode(
            "ProbeLot", "Branch",
            Methods: [
                new MethodDefinitionNode(
                    "Probe",
                    new PrimitiveTypeReference(PrimitiveType.Boolean),
                    Parameters: [age],
                    Body: new Return(body),
                    IsStatic: true)
            ]));

    private static Node Call(params Node[] arguments) =>
        new Invoke(new Member(new TypeReference("Branch.ProbeLot"), "Probe"), arguments);

    private static bool Run(ITypeDefinitionProvider types, params Node[] arguments) {
        using var exec = Interpreter.Execute(Interpreter.Compile(Call(arguments), types));
        return exec.GetValue<bool>();
    }

    private static Parameter OptionalAge() =>
        new("age", new OptionalTypeReference(TypeReference.To<long>()), DefaultValue: new Constant(null));

    [Test]
    public async Task AstMethod_OptionalScalar_PresentZero_NotNull() {
        var age = OptionalAge();
        var types = ProbeType(age, new NotEqual(age, new Constant(null)));
        await Assert.That(Run(types, new Constant(0L))).IsTrue();
    }

    [Test]
    public async Task AstMethod_OptionalScalar_Absent_IsNull() {
        var age = OptionalAge();
        var types = ProbeType(age, new Equal(age, new Constant(null)));
        await Assert.That(Run(types)).IsTrue();
    }

    [Test]
    public async Task AstMethod_OptionalScalar_NullArgument_IsNull() {
        var age = OptionalAge();
        var types = ProbeType(age, new Equal(age, new Constant(null)));
        await Assert.That(Run(types, new Constant(null))).IsTrue();
    }

    /// <summary>An optional parameter is a heap handle; assigning it to an optional
    /// member stores the value, not the handle.</summary>
    [Test]
    public async Task Constructor_OptionalScalarMember_FromOptionalParam_StoresValue() {
        var limit = OptionalAge();
        var types = Analyze(new TypeDefinitionNode(
            "CopyLot", "Branch",
            Constructors: [
                new ConstructorDefinitionNode(
                    [limit],
                    new Block([
                        new Assignment(new Member(new ThisReference(), "Limit"), limit)
                    ]))
            ],
            Properties: [
                new PropertyDefinitionNode("Limit", new OptionalTypeReference(TypeReference.To<long>()))
            ]));
        using var exec = Interpreter.Execute(
            Interpreter.Compile(new New(new TypeReference("Branch.CopyLot"), new Constant(41L)), types));
        var bag = exec.GetValue<Dictionary<string, object?>>();
        await Assert.That(bag!["Limit"]).IsEqualTo(41L);
    }

    [Test]
    public async Task Constructor_OptionalScalarMember_FromOmittedOptional_StoresNull() {
        var limit = OptionalAge();
        var types = Analyze(new TypeDefinitionNode(
            "OmitLot", "Branch",
            Constructors: [
                new ConstructorDefinitionNode(
                    [limit],
                    new Block([
                        new Assignment(new Member(new ThisReference(), "Limit"), limit)
                    ]))
            ],
            Properties: [
                new PropertyDefinitionNode("Limit", new OptionalTypeReference(TypeReference.To<long>()))
            ]));
        using var exec = Interpreter.Execute(
            Interpreter.Compile(new New(new TypeReference("Branch.OmitLot")), types));
        var bag = exec.GetValue<Dictionary<string, object?>>();
        await Assert.That(bag!["Limit"]).IsNull();
    }

    /// <summary>A scalar with no default is required: omitting it is an analysis
    /// error, never a silent zero or null word.</summary>
    [Test]
    public async Task Constructor_RequiredScalar_Omitted_IsAnalysisError() {
        var types = Analyze(new TypeDefinitionNode(
            "RequiredLot", "Branch",
            Constructors: [
                new ConstructorDefinitionNode(
                    [new Parameter("count", TypeReference.To<long>())],
                    new Block([]))
            ]));
        await Assert.That(() => Interpreter.Compile(new New(new TypeReference("Branch.RequiredLot")), types))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("no matching constructor");
    }

    [Test]
    public async Task AstMethod_RequiredScalar_Omitted_IsAnalysisError() {
        var age = new Parameter("age", TypeReference.To<long>());
        var types = ProbeType(age, new NotEqual(age, new Constant(null)));
        await Assert.That(() => Interpreter.Compile(Call(), types))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("no matching member");
    }
}