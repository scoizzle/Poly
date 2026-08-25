using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;

namespace Poly.Tests.Interpretation;

/// <summary>Language-VM oracles: Interpreter.Compile on Syntax nodes, no DomainModeling.</summary>
public class LanguageVmTests {
    [Test]
    public async Task Compile_UnresolvedMember_Throws() {
        await Assert.That(() => Interpreter.Compile(new Member(new Parameter("entity"), "Nope")))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Compile_StaticThis_Throws() {
        var thisReference = new ThisReference();
        var typeNode = new TypeDefinitionNode(
            "Widget",
            Methods: [
                new MethodDefinitionNode(
                    "Bad",
                    new PrimitiveTypeReference(PrimitiveType.String),
                    Body: thisReference,
                    IsStatic: true)
            ]);
        await Assert.That(() => Interpreter.Compile(typeNode)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Comment_AsProgram_IsVoid() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new Comment("note")));
        await Assert.That(exec.Result.IsVoid).IsTrue();
    }

    [Test]
    public async Task Comment_AsValue_CompileRejected() {
        await Assert.That(() => Interpreter.Compile(new Add(new Comment("x"), new Constant(1L))))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Await_CompileRejected() {
        await Assert.That(() => Interpreter.Compile(new Await(new Constant(1L))))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ParameterReference_CompileRejected() {
        await Assert.That(() => Interpreter.Compile(new ParameterReference()))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task TypeCast_IntToDouble_BitcastResult() {
        var node = new TypeCast(new Constant(42), TypeReference.To<double>());
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<double>()).IsEqualTo(42.0);
    }

    [Test]
    public async Task TypeCast_DoubleToInt_Truncates() {
        var node = new TypeCast(new Constant(3.9), TypeReference.To<int>());
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<int>()).IsEqualTo(3);
    }

    [Test]
    public async Task TypeAs_MatchingReference_KeepsObject() {
        var node = new TypeAs(new Constant("hello"), TypeReference.To<object>());
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<string>()).IsEqualTo("hello");
    }

    [Test]
    public async Task TypeAs_Mismatch_IsNull() {
        var node = new TypeAs(new Constant("hello"), TypeReference.To<Uri>());
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<object>()).IsNull();
    }

    [Test]
    public async Task TypeOf_String_ReturnsRuntimeType() {
        var node = new TypeOf(TypeReference.To<string>());
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<Type>()).IsEqualTo(typeof(string));
    }

    [Test]
    public async Task Default_Untyped_IsZero() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new Default()));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(0L);
    }

    [Test]
    public async Task Default_String_IsAbiNull() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new Default(TypeReference.To<string>())));
        await Assert.That(exec.GetValue<object>()).IsNull();
    }

    [Test]
    public async Task ThrowExpression_ThrowsOperand() {
        var node = new ThrowExpression(new New(TypeReference.To<InvalidOperationException>()));
        await Assert.That(() => {
            using var exec = Interpreter.Execute(Interpreter.Compile(node));
        }).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Lambda_Invoke_AddsArguments() {
        var x = new Parameter("x", TypeReference.To<long>());
        var y = new Parameter("y", TypeReference.To<long>());
        var node = new Invoke(new Lambda([x, y], new Add(x, y)), new Constant(3L), new Constant(4L));
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(7L);
    }

    [Test]
    public async Task ClrMethod_ToString_OnConstantString() {
        var node = new Invoke(new Member(new Constant("hi"), "ToString"));
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<string>()).IsEqualTo("hi");
    }
}