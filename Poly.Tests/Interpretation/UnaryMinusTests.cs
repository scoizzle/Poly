using Poly.Interpretation;
using Poly.Tests.TestHelpers;

using Expr = System.Linq.Expressions.Expression;

namespace Poly.Tests.Interpretation;

public class UnaryMinusTests {
    [Test]
    public async Task UnaryMinus_WithPositiveInteger_ReturnsNegative() {
        // Arrange
        var node = new UnaryMinus(Wrap(42));

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<int>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(-42);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(-42L);
    }

    [Test]
    public async Task UnaryMinus_WithNegativeInteger_ReturnsPositive() {
        // Arrange
        var node = new UnaryMinus(Wrap(-99));

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<int>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(99);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(99L);
    }

    [Test]
    public async Task UnaryMinus_WithZero_ReturnsZero() {
        // Arrange
        var node = new UnaryMinus(Wrap(0));

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<int>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(0);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(0L);
    }

    [Test]
    public async Task UnaryMinus_WithDouble_NegatesCorrectly() {
        // Arrange
        var node = new UnaryMinus(Wrap(3.14));

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<double>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(-3.14);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<double>()).IsEqualTo(-3.14);
    }

    [Test]
    public async Task UnaryMinus_WithParameter_EvaluatesCorrectly() {
        // Arrange
        var param = new Parameter("x", TypeReference.To<int>());
        var node = new UnaryMinus(param);

        // Act
        var compiled = node.CompileLambda<Func<int, int>>((param, typeof(int)));

        // Assert
        await Assert.That(compiled(10)).IsEqualTo(-10);
        await Assert.That(compiled(-5)).IsEqualTo(5);
        await Assert.That(compiled(0)).IsEqualTo(0);
        var program = Interpreter.Compile(node);
        using var exec10 = Interpreter.Execute(program, s => s.SetArgs(10));
        await Assert.That(exec10.GetValue<long>()).IsEqualTo(-10L);
        using var execNeg = Interpreter.Execute(program, s => s.SetArgs(-5));
        await Assert.That(execNeg.GetValue<long>()).IsEqualTo(5L);
        using var exec0 = Interpreter.Execute(program, s => s.SetArgs(0));
        await Assert.That(exec0.GetValue<long>()).IsEqualTo(0L);
    }

    [Test]
    public async Task UnaryMinus_DoubleNegation_ReturnsOriginalValue() {
        // Arrange
        var param = new Parameter("x", TypeReference.To<int>());
        var node = new UnaryMinus(new UnaryMinus(param));

        // Act
        var compiled = node.CompileLambda<Func<int, int>>((param, typeof(int)));

        // Assert
        await Assert.That(compiled(42)).IsEqualTo(42);
        await Assert.That(compiled(-7)).IsEqualTo(-7);
        var program = Interpreter.Compile(node);
        using var exec42 = Interpreter.Execute(program, s => s.SetArgs(42));
        await Assert.That(exec42.GetValue<long>()).IsEqualTo(42L);
        using var execNeg = Interpreter.Execute(program, s => s.SetArgs(-7));
        await Assert.That(execNeg.GetValue<long>()).IsEqualTo(-7L);
    }

    [Test]
    public async Task UnaryMinus_WithArithmeticExpression_EvaluatesCorrectly() {
        // Arrange
        var param = new Parameter("x", TypeReference.To<int>());
        var node = new UnaryMinus(new Add(param, Wrap(5)));

        // Act
        var compiled = node.CompileLambda<Func<int, int>>((param, typeof(int)));

        // Assert
        await Assert.That(compiled(10)).IsEqualTo(-15);
        await Assert.That(compiled(-3)).IsEqualTo(-2);
        var program = Interpreter.Compile(node);
        using var exec10 = Interpreter.Execute(program, s => s.SetArgs(10));
        await Assert.That(exec10.GetValue<long>()).IsEqualTo(-15L);
        using var execNeg = Interpreter.Execute(program, s => s.SetArgs(-3));
        await Assert.That(execNeg.GetValue<long>()).IsEqualTo(-2L);
    }

    [Test]
    public async Task UnaryMinus_ToString_ReturnsExpectedFormat() {
        // Arrange
        var node = new UnaryMinus(Wrap(42));

        // Act
        var result = node.ToString();

        // Assert
        await Assert.That(result).IsEqualTo("-42");
    }

    [Test]
    public async Task UnaryMinus_WithNullArgument_AllowsNull() {
        // Act
        var node = new UnaryMinus(null!);

        // Assert
        await Assert.That(node).IsNotNull();
    }
}