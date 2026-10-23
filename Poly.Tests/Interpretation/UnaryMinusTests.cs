using Poly.Interpretation;

namespace Poly.Tests.Interpretation;

public class UnaryMinusTests {
    [Test]
    public async Task UnaryMinus_WithPositiveInteger_ReturnsNegative() {
        // Arrange
        var node = new UnaryMinus(Wrap(42));

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        // Assert
        await Assert.That(exec.GetValue<long>()).IsEqualTo(-42L);
    }

    [Test]
    public async Task UnaryMinus_WithNegativeInteger_ReturnsPositive() {
        // Arrange
        var node = new UnaryMinus(Wrap(-99));

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        // Assert
        await Assert.That(exec.GetValue<long>()).IsEqualTo(99L);
    }

    [Test]
    public async Task UnaryMinus_WithZero_ReturnsZero() {
        // Arrange
        var node = new UnaryMinus(Wrap(0));

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        // Assert
        await Assert.That(exec.GetValue<long>()).IsEqualTo(0L);
    }

    [Test]
    public async Task UnaryMinus_WithDouble_NegatesCorrectly() {
        // Arrange
        var node = new UnaryMinus(Wrap(3.14));

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        // Assert
        await Assert.That(exec.GetValue<double>()).IsEqualTo(-3.14);
    }

    [Test]
    public async Task UnaryMinus_WithParameter_EvaluatesCorrectly() {
        // Arrange
        var param = new Parameter("x", TypeReference.To<int>());
        var node = new UnaryMinus(param);

        // Act
        var program = Interpreter.Compile(node);
        using var exec10 = Interpreter.Execute(program, s => s.SetArgs(10));
        using var execNeg = Interpreter.Execute(program, s => s.SetArgs(-5));
        using var exec0 = Interpreter.Execute(program, s => s.SetArgs(0));

        // Assert
        await Assert.That(exec10.GetValue<long>()).IsEqualTo(-10L);
        await Assert.That(execNeg.GetValue<long>()).IsEqualTo(5L);
        await Assert.That(exec0.GetValue<long>()).IsEqualTo(0L);
    }

    [Test]
    public async Task UnaryMinus_DoubleNegation_ReturnsOriginalValue() {
        // Arrange
        var param = new Parameter("x", TypeReference.To<int>());
        var node = new UnaryMinus(new UnaryMinus(param));

        // Act
        var program = Interpreter.Compile(node);
        using var exec42 = Interpreter.Execute(program, s => s.SetArgs(42));
        using var execNeg = Interpreter.Execute(program, s => s.SetArgs(-7));

        // Assert
        await Assert.That(exec42.GetValue<long>()).IsEqualTo(42L);
        await Assert.That(execNeg.GetValue<long>()).IsEqualTo(-7L);
    }

    [Test]
    public async Task UnaryMinus_WithArithmeticExpression_EvaluatesCorrectly() {
        // Arrange
        var param = new Parameter("x", TypeReference.To<int>());
        var node = new UnaryMinus(new Add(param, Wrap(5)));

        // Act
        var program = Interpreter.Compile(node);
        using var exec10 = Interpreter.Execute(program, s => s.SetArgs(10));
        using var execNeg = Interpreter.Execute(program, s => s.SetArgs(-3));

        // Assert
        await Assert.That(exec10.GetValue<long>()).IsEqualTo(-15L);
        await Assert.That(execNeg.GetValue<long>()).IsEqualTo(-2L);
    }

    [Test]
    public async Task UnaryMinus_NestedInAddition_EvaluatesCorrectly() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new Add(Wrap(10), new UnaryMinus(Wrap(5)))));

        await Assert.That(exec.GetValue<long>()).IsEqualTo(5L);
    }
}