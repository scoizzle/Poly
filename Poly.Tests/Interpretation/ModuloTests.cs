using Poly.Interpretation;

namespace Poly.Tests.Interpretation;

public class ModuloTests {
    [Test]
    public async Task Modulo_WithIntegers_ReturnsRemainder() {
        // Arrange
        var node = new Modulo(Wrap(17), Wrap(5));

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        // Assert
        await Assert.That(exec.GetValue<long>()).IsEqualTo(2L);
    }

    [Test]
    public async Task Modulo_WithExactDivision_ReturnsZero() {
        // Arrange
        var node = new Modulo(Wrap(20), Wrap(5));

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        // Assert
        await Assert.That(exec.GetValue<long>()).IsEqualTo(0L);
    }

    [Test]
    public async Task Modulo_WithDoubles_ReturnsRemainder() {
        // Arrange
        var node = new Modulo(Wrap(5.5), Wrap(2.0));

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        // Assert
        await Assert.That(exec.GetValue<double>()).IsEqualTo(1.5);
    }

    [Test]
    public async Task Modulo_WithParameters_EvaluatesCorrectly() {
        // Arrange
        var param1 = new Parameter("a", TypeReference.To<int>());
        var param2 = new Parameter("b", TypeReference.To<int>());
        var node = new Modulo(param1, param2);

        // Act
        var program = Interpreter.Compile(node);
        using var exec = Interpreter.Execute(program, s => s.SetArgs(17, 5));

        // Assert
        await Assert.That(exec.GetValue<long>()).IsEqualTo(2L);
    }

    [Test]
    public async Task Modulo_WithNegativeNumbers_ReturnsCorrectRemainder() {
        // Arrange
        var node = new Modulo(Wrap(-17), Wrap(5));

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        // Assert
        await Assert.That(exec.GetValue<long>()).IsEqualTo(-2L);
    }

    [Test]
    public async Task Modulo_ToString_ReturnsExpectedFormat() {
        // Arrange
        var node = new Modulo(Wrap(17), Wrap(5));

        // Act
        var result = node.ToString();

        // Assert
        await Assert.That(result).Contains("%");
    }

    [Test]
    public async Task Modulo_WithNullArguments_ThrowsArgumentNullException() {
        // Act & Assert
        await Assert.That(() => new Modulo(null!, Wrap(5))).Throws<ArgumentNullException>();
    }
}