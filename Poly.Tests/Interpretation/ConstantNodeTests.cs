using Poly.Interpretation;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Unit tests for Constant AST nodes and their LINQ expression compilation.
/// </summary>
public class ConstantNodeTests {
    [Test]
    public async Task Constant_IntegerValue_CompilesAndExecutes() {
        // Arrange
        var node = new Constant(42);

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        // Assert
        await Assert.That(exec.GetValue<long>()).IsEqualTo(42L);
    }

    [Test]
    public async Task Constant_StringValue_CompilesAndExecutes() {
        // Arrange
        var node = new Constant("hello");

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        // Assert
        await Assert.That(exec.GetValue<string>()).IsEqualTo("hello");
    }

    [Test]
    public async Task Constant_DoubleValue_CompilesAndExecutes() {
        // Arrange
        var node = new Constant(3.14);

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        // Assert
        await Assert.That(exec.GetValue<double>()).IsEqualTo(3.14);
    }

    [Test]
    public async Task Constant_BooleanTrue_CompilesAndExecutes() {
        // Arrange
        var node = new Constant(true);

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        // Assert
        await Assert.That(exec.GetValue<bool>()).IsTrue();
    }

    [Test]
    public async Task Constant_BooleanFalse_CompilesAndExecutes() {
        // Arrange
        var node = new Constant(false);

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        // Assert
        await Assert.That(exec.GetValue<bool>()).IsFalse();
    }

    [Test]
    public async Task Constant_NullValue_CompilesAndExecutes() {
        // Arrange
        var node = new Constant(null);

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        // Assert
        await Assert.That(exec.GetValue<object>()).IsNull();
    }

    [Test]
    public async Task Constant_DecimalValue_CompilesAndExecutes() {
        // Arrange
        var node = new Constant(99.99m);

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        // Assert
        await Assert.That(exec.GetValue<decimal>()).IsEqualTo(99.99m);
    }

    [Test]
    public async Task Constant_DateTime_CompilesAndExecutes() {
        // Arrange
        var expected = new DateTime(2024, 1, 15, 10, 30, 0);
        var node = new Constant(expected);

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        // Assert
        await Assert.That(exec.GetValue<DateTime>()).IsEqualTo(expected);
    }

    [Test]
    public async Task Constant_ComplexObject_CompilesAndExecutes() {
        // Arrange
        var expected = new List<int> { 1, 2, 3 };
        var node = new Constant(expected);

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        // Assert
        await Assert.That(exec.GetValue<List<int>>()).IsEqualTo(expected);
        await Assert.That(exec.GetValue<List<int>>()!.Count).IsEqualTo(3);
    }
}