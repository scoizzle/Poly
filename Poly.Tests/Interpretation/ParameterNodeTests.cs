using Poly.Interpretation;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Unit tests for Parameter AST nodes and their LINQ expression compilation.
/// </summary>
public class ParameterNodeTests {
    [Test]
    public async Task Parameter_IntType_CompilesAndExecutesWithValue() {
        // Arrange
        var param = new Parameter("x", TypeReference.To<int>());

        // Act
        var program = Interpreter.Compile(param);
        using var exec42 = Interpreter.Execute(program, s => s.SetArgs(42));
        using var exec100 = Interpreter.Execute(program, s => s.SetArgs(100));

        // Assert
        await Assert.That(exec42.GetValue<long>()).IsEqualTo(42L);
        await Assert.That(exec100.GetValue<long>()).IsEqualTo(100L);
    }

    [Test]
    public async Task Parameter_StringType_CompilesAndExecutesWithValue() {
        // Arrange
        var param = new Parameter("name", TypeReference.To<string>());

        // Act
        var program = Interpreter.Compile(param);
        using var execHello = Interpreter.Execute(program, s => s.SetArgs("hello"));
        using var execWorld = Interpreter.Execute(program, s => s.SetArgs("world"));

        // Assert
        await Assert.That(execHello.GetValue<string>()).IsEqualTo("hello");
        await Assert.That(execWorld.GetValue<string>()).IsEqualTo("world");
    }

    [Test]
    public async Task Parameter_DoubleType_CompilesAndExecutesWithValue() {
        // Arrange
        var param = new Parameter("value", TypeReference.To<double>());

        // Act
        var program = Interpreter.Compile(param);
        using var execPi = Interpreter.Execute(program, s => s.SetArgs(3.14));
        using var execE = Interpreter.Execute(program, s => s.SetArgs(2.71));

        // Assert
        await Assert.That(execPi.GetValue<double>()).IsEqualTo(3.14);
        await Assert.That(execE.GetValue<double>()).IsEqualTo(2.71);
    }

    [Test]
    public async Task Parameter_BoolType_CompilesAndExecutesWithValue() {
        // Arrange
        var param = new Parameter("flag", TypeReference.To<bool>());

        // Act
        var program = Interpreter.Compile(param);
        using var execTrue = Interpreter.Execute(program, s => s.SetArgs(true));
        using var execFalse = Interpreter.Execute(program, s => s.SetArgs(false));

        // Assert
        await Assert.That(execTrue.GetValue<bool>()).IsTrue();
        await Assert.That(execFalse.GetValue<bool>()).IsFalse();
    }

    [Test]
    public async Task Parameter_MultipleParameters_CompilesAndExecutes() {
        // Arrange
        var x = new Parameter("x", TypeReference.To<int>());
        var y = new Parameter("y", TypeReference.To<int>());

        // Act - Just return the first parameter
        var program = Interpreter.Compile(x);
        using var exec10 = Interpreter.Execute(program, s => s.SetArgs(10));
        using var exec5 = Interpreter.Execute(program, s => s.SetArgs(5));

        // Assert
        await Assert.That(exec10.GetValue<long>()).IsEqualTo(10L);
        await Assert.That(exec5.GetValue<long>()).IsEqualTo(5L);
    }

    [Test]
    public async Task Parameter_WithoutTypeHint_CompilesAsObject() {
        // Arrange
        var param = new Parameter("value");

        // Act
        var program = Interpreter.Compile(param);
        using var execInt = Interpreter.Execute(program, s => s.SetArgs(42));
        using var execStr = Interpreter.Execute(program, s => s.SetArgs("test"));

        // Assert - Can accept any object
        await Assert.That(execInt.GetValue<long>()).IsEqualTo(42L);
        await Assert.That(execStr.GetValue<string>()).IsEqualTo("test");
    }

    [Test]
    public async Task Parameter_SameParameterTwice_UsesSameArgument() {
        // Arrange
        var param = new Parameter("x", TypeReference.To<int>());
        var node = new Add(param, param);

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(node), s => s.SetArgs(7));

        // Assert - both uses of the parameter resolve to the one argument
        await Assert.That(exec.GetValue<long>()).IsEqualTo(14L);
    }
}