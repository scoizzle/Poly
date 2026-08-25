using Poly.Interpretation;
using Poly.Tests.TestHelpers;

using Expr = System.Linq.Expressions.Expression;

namespace Poly.Tests.Interpretation;

public class NumericTypePromotionTests {
    [Test]
    public async Task NumericTypePromotion_Add_IntAndDouble_ReturnsDouble() {
        // Arrange
        var node = new Add(Wrap(10), Wrap(3.14));

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<double>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(Math.Abs(result - 13.14) < 0.01).IsTrue();
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(Math.Abs(exec.GetValue<double>() - 13.14) < 0.01).IsTrue();
    }

    [Test]
    public async Task NumericTypePromotion_Multiply_FloatAndInt_ReturnsFloat() {
        // Arrange
        var node = new Multiply(Wrap(2.5f), Wrap(4));

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<float>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(10.0f);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<float>()).IsEqualTo(10.0f);
    }

    [Test]
    public async Task NumericTypePromotion_Subtract_LongAndInt_ReturnsLong() {
        // Arrange
        var node = new Subtract(Wrap(100L), Wrap(30));

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<long>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(70L);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(70L);
    }

    [Test]
    public async Task NumericTypePromotion_Divide_DecimalAndInt_ReturnsDecimal() {
        // Arrange
        var node = new Divide(Wrap(100m), Wrap(4));

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<decimal>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(25m);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<decimal>()).IsEqualTo(25m);
    }

    [Test]
    public async Task NumericTypePromotion_Modulo_DoubleAndFloat_ReturnsDouble() {
        // Arrange
        var node = new Modulo(Wrap(10.0), Wrap(3.0f));

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<double>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(Math.Abs(result - 1.0) < 0.01).IsTrue();
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(Math.Abs(exec.GetValue<double>() - 1.0) < 0.01).IsTrue();
    }

    [Test]
    public async Task NumericTypePromotion_Add_TwoInts_ReturnsInt() {
        // Arrange
        var node = new Add(Wrap(10), Wrap(20));

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<int>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(30);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(30L);
    }

    [Test]
    public async Task NumericTypePromotion_Add_ByteAndShort_ReturnsInt() {
        // Arrange
        var node = new Add(Wrap((byte)5), Wrap((short)10));

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<int>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(15);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(15L);
    }

    [Test]
    public async Task NumericTypePromotion_Multiply_UIntAndLong_ReturnsLong() {
        // Arrange
        var node = new Multiply(Wrap(5u), Wrap(10L));

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<long>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(50L);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(50L);
    }

    [Test]
    public async Task NumericTypePromotion_Add_ULongAndInt_ReturnsULong() {
        // Arrange
        var node = new Add(Wrap(100UL), Wrap(50));

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<ulong>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(150UL);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<ulong>()).IsEqualTo(150UL);
    }

    [Test]
    public async Task NumericTypePromotion_Add_WithParameters_PromotesCorrectly() {
        // Arrange
        var param1 = new Parameter("a", TypeReference.To<int>());
        var param2 = new Parameter("b", TypeReference.To<double>());
        var node = new Add(param1, param2);

        // Act
        var compiled = node.CompileLambda<Func<int, double, double>>((param1, typeof(int)), (param2, typeof(double)));
        var result = compiled(10, 3.14);

        // Assert
        await Assert.That(Math.Abs(result - 13.14) < 0.01).IsTrue();
        var program = Interpreter.Compile(node);
        using var exec = Interpreter.Execute(program, s => s.SetArgs(10, 3.14));
        await Assert.That(Math.Abs(exec.GetValue<double>() - 13.14) < 0.01).IsTrue();
    }
}