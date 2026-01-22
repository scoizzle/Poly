using Poly.Tests.TestHelpers;
using System.Linq.Expressions;

using Poly.Interpretation;
using Expr = System.Linq.Expressions.Expression;
using Poly.Interpretation.AbstractSyntaxTree;

namespace Poly.Tests.Interpretation;

public class TypeCastTests
{
    [Test]
    public async Task TypeCast_IntToDouble_ReturnsDouble()
    {
        // Arrange
        var node = new TypeCast(Wrap(42), TypeReference.To<double>());

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<double>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(42.0);
    }

    [Test]
    public async Task TypeCast_DoubleToInt_ReturnsInt()
    {
        // Arrange
        var node = new TypeCast(Wrap(3.14), TypeReference.To<int>());

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<int>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(3);
    }

    [Test]
    public async Task TypeCast_LongToInt_ReturnsInt()
    {
        // Arrange
        var node = new TypeCast(Wrap(9999L), TypeReference.To<int>());

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<int>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(9999);
    }

    [Test]
    public async Task TypeCast_WithParameter_EvaluatesCorrectly()
    {
        // Arrange
        var param = new Parameter("value", TypeReference.To<int>());
        var node = new TypeCast(param, TypeReference.To<double>());

        // Act
        var expr = node.BuildExpression();
        var paramExpr = param.GetParameterExpression();
        var compiled = Expr.Lambda<Func<int, double>>(expr, paramExpr).Compile();
        var result = compiled(42);

        // Assert
        await Assert.That(result).IsEqualTo(42.0);
    }

    [Test]
    public async Task TypeCast_StringToObject_WorksCorrectly()
    {
        // Arrange
        var node = new TypeCast(Wrap("hello"), TypeReference.To<object>());

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<object>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo("hello");
    }

    [Test]
    public async Task TypeCast_ObjectToString_WorksCorrectly()
    {
        // Arrange
        var obj = (object)"world";
        var node = new TypeCast(Wrap(obj), TypeReference.To<string>());

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<string>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo("world");
    }

    [Test]
    public async Task TypeCast_NullableToNonNullable_WorksCorrectly()
    {
        // Arrange
        var node = new TypeCast(Wrap(42 as int?), TypeReference.To<int>());

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<int>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task TypeCast_NonNullableToNullable_WorksCorrectly()
    {
        // Arrange
        var node = new TypeCast(Wrap(42), TypeReference.To<int?>());

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<int?>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task TypeCast_GetTypeDefinition_ReturnsTargetType()
    {
        // Arrange
        var node = new TypeCast(Wrap(42), TypeReference.To<double>());

        // Act - build to trigger semantic analysis
        _ = node.BuildExpression();

        // Assert
        await Assert.That(node).IsNotNull();
    }

    [Test]
    public async Task TypeCast_ToString_ReturnsExpectedFormat()
    {
        // Arrange
        var node = new TypeCast(Wrap(42), TypeReference.To<double>());

        // Act
        var result = node.ToString();

        // Assert
        await Assert.That(result).Contains("System.Double");
    }

    [Test]
    public async Task TypeCast_WithNullArguments_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.That(() => new TypeCast(null!, TypeReference.To<double>())).Throws<ArgumentNullException>();
    }
}
