using Poly.Interpretation;
using Poly.Introspection;

namespace Poly.Tests.Interpretation.Vm.Abi;

/// <summary>
/// Assigning a scalar bool to a <c>bool?</c> local (#330 R4). The scalar bool word is
/// 0/1, so the optional store boxes that test; <c>Convert(long, bool)</c> does not exist.
/// </summary>
public class AbiEmitterOptionalBoolAssignTests {
    private static ExecutionResult Execute(Node body, params object?[] args) =>
        Interpreter.Execute(Interpreter.Compile(body), s => s.SetArgs(args));

    private static Node AssignScalarBool(Node scalar) {
        var flag = new Parameter("flag", new OptionalTypeReference(TypeReference.To<bool>()));
        var local = new Variable("local");
        return new Block([
            // The first write declares the local as bool?.
            new Assignment(local, flag),
            new Assignment(local, scalar),
            local
        ], [local]);
    }

    [Test]
    public async Task OptionalBoolLocal_AssignScalarTrue_IsPresentTrue() {
        using var exec = Execute(
            AssignScalarBool(new Equal(new Constant(1L), new Constant(1L))), true);
        await Assert.That(exec.Result.Value is true).IsTrue();
    }

    [Test]
    public async Task OptionalBoolLocal_AssignScalarFalse_IsPresentFalse() {
        using var exec = Execute(
            AssignScalarBool(new Equal(new Constant(1L), new Constant(2L))), true);
        await Assert.That(exec.Result.Value is false).IsTrue();
    }
}
