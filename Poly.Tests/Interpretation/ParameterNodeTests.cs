using Poly.Interpretation;
using Poly.Tests.TestHelpers;

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
        var compiled = param.CompileLambda<Func<int, int>>((param, typeof(int)));

        // Assert
        await Assert.That(compiled(42)).IsEqualTo(42);
        await Assert.That(compiled(100)).IsEqualTo(100);
        var program = Interpreter.Compile(param);
        using var exec42 = Interpreter.Execute(program, s => s.SetArgs(42));
        await Assert.That(exec42.GetValue<long>()).IsEqualTo(42L);
        using var exec100 = Interpreter.Execute(program, s => s.SetArgs(100));
        await Assert.That(exec100.GetValue<long>()).IsEqualTo(100L);
    }

    [Test]
    public async Task Parameter_StringType_CompilesAndExecutesWithValue() {
        // Arrange
        var param = new Parameter("name", TypeReference.To<string>());

        // Act
        var compiled = param.CompileLambda<Func<string, string>>((param, typeof(string)));

        // Assert
        await Assert.That(compiled("hello")).IsEqualTo("hello");
        await Assert.That(compiled("world")).IsEqualTo("world");
        var program = Interpreter.Compile(param);
        using var execHello = Interpreter.Execute(program, s => s.SetArgs("hello"));
        await Assert.That(execHello.GetValue<string>()).IsEqualTo("hello");
        using var execWorld = Interpreter.Execute(program, s => s.SetArgs("world"));
        await Assert.That(execWorld.GetValue<string>()).IsEqualTo("world");
    }

    [Test]
    public async Task Parameter_DoubleType_CompilesAndExecutesWithValue() {
        // Arrange
        var param = new Parameter("value", TypeReference.To<double>());

        // Act
        var compiled = param.CompileLambda<Func<double, double>>((param, typeof(double)));

        // Assert
        await Assert.That(compiled(3.14)).IsEqualTo(3.14);
        await Assert.That(compiled(2.71)).IsEqualTo(2.71);
        var program = Interpreter.Compile(param);
        using var execPi = Interpreter.Execute(program, s => s.SetArgs(3.14));
        await Assert.That(execPi.GetValue<double>()).IsEqualTo(3.14);
        using var execE = Interpreter.Execute(program, s => s.SetArgs(2.71));
        await Assert.That(execE.GetValue<double>()).IsEqualTo(2.71);
    }

    [Test]
    public async Task Parameter_BoolType_CompilesAndExecutesWithValue() {
        // Arrange
        var param = new Parameter("flag", TypeReference.To<bool>());

        // Act
        var compiled = param.CompileLambda<Func<bool, bool>>((param, typeof(bool)));

        // Assert
        await Assert.That(compiled(true)).IsTrue();
        await Assert.That(compiled(false)).IsFalse();
        var program = Interpreter.Compile(param);
        using var execTrue = Interpreter.Execute(program, s => s.SetArgs(true));
        await Assert.That(execTrue.GetValue<bool>()).IsTrue();
        using var execFalse = Interpreter.Execute(program, s => s.SetArgs(false));
        await Assert.That(execFalse.GetValue<bool>()).IsFalse();
    }

    [Test]
    public async Task Parameter_MultipleParameters_CompilesAndExecutes() {
        // Arrange
        var x = new Parameter("x", TypeReference.To<int>());
        var y = new Parameter("y", TypeReference.To<int>());

        // Act - Just return the first parameter
        var compiled = x.CompileLambda<Func<int, int, int>>((x, typeof(int)), (y, typeof(int)));

        // Assert
        await Assert.That(compiled(10, 20)).IsEqualTo(10);
        await Assert.That(compiled(5, 15)).IsEqualTo(5);
        var program = Interpreter.Compile(x);
        using var exec10 = Interpreter.Execute(program, s => s.SetArgs(10));
        await Assert.That(exec10.GetValue<long>()).IsEqualTo(10L);
        using var exec5 = Interpreter.Execute(program, s => s.SetArgs(5));
        await Assert.That(exec5.GetValue<long>()).IsEqualTo(5L);
    }

    [Test]
    public async Task Parameter_WithoutTypeHint_CompilesAsObject() {
        // Arrange
        var param = new Parameter("value");

        // Act
        var compiled = param.CompileLambda<Func<object, object>>((param, typeof(object)));

        // Assert - Can accept any object
        await Assert.That(compiled(42)).IsEqualTo(42);
        await Assert.That(compiled("test")).IsEqualTo("test");
        var program = Interpreter.Compile(param);
        using var execInt = Interpreter.Execute(program, s => s.SetArgs(42));
        await Assert.That(execInt.GetValue<long>()).IsEqualTo(42L);
        using var execStr = Interpreter.Execute(program, s => s.SetArgs("test"));
        await Assert.That(execStr.GetValue<string>()).IsEqualTo("test");
    }

    [Test]
    public async Task Parameter_SameParameterTwice_ReturnsSameExpression() {
        // Arrange
        var param = new Parameter("x", TypeReference.To<int>());
        var node = new Add(param, param);

        // Act
        var (expr, parameters) = node.BuildExpressionWithParameters((param, typeof(int)));
        var binary = (System.Linq.Expressions.BinaryExpression)expr;

        // Assert - Both uses of the parameter should share the same expression instance
        await Assert.That(ReferenceEquals(binary.Left, binary.Right)).IsTrue();
        await Assert.That(ReferenceEquals(binary.Left, parameters[0])).IsTrue();
    }
}