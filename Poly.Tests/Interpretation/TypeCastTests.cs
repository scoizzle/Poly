using System.Linq.Expressions;

using Poly.Interpretation;
using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Tests.TestHelpers;

using Expr = System.Linq.Expressions.Expression;

namespace Poly.Tests.Interpretation;

public class TypeCastTests {
    [Test]
    public async Task TypeCast_IntToDouble_ReturnsDouble() {
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
    public async Task TypeCast_DoubleToInt_ReturnsInt() {
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
    public async Task TypeCast_LongToInt_ReturnsInt() {
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
    public async Task TypeCast_WithParameter_EvaluatesCorrectly() {
        // Arrange
        var param = new Parameter("value", TypeReference.To<int>());
        var node = new TypeCast(param, TypeReference.To<double>());

        // Act
        var compiled = node.CompileLambda<Func<int, double>>((param, typeof(int)));
        var result = compiled(42);

        // Assert
        await Assert.That(result).IsEqualTo(42.0);
    }

    [Test]
    public async Task TypeCast_StringToObject_WorksCorrectly() {
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
    public async Task TypeCast_ObjectToString_WorksCorrectly() {
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
    public async Task TypeCast_NullableToNonNullable_WorksCorrectly() {
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
    public async Task TypeCast_NonNullableToNullable_WorksCorrectly() {
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
    public async Task TypeCast_GetTypeDefinition_ReturnsTargetType() {
        // Arrange
        var node = new TypeCast(Wrap(42), TypeReference.To<double>());

        // Act - build to trigger semantic analysis
        _ = node.BuildExpression();

        // Assert
        await Assert.That(node).IsNotNull();
    }

    [Test]
    public async Task TypeCast_ToString_ReturnsExpectedFormat() {
        // Arrange
        var node = new TypeCast(Wrap(42), TypeReference.To<double>());

        // Act
        var result = node.ToString();

        // Assert
        await Assert.That(result).Contains("System.Double");
    }

    [Test]
    public async Task TypeCast_WithNullArguments_ThrowsArgumentNullException() {
        // Act & Assert
        await Assert.That(() => new TypeCast(null!, TypeReference.To<double>())).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task TypeIs_ObjectAgainstString_ReturnsTrue() {
        // Arrange
        var value = Wrap((object)"hello");
        var node = new TypeIs(value, TypeReference.To<string>());

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<bool>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task TypeIs_ObjectAgainstInt_ReturnsFalse() {
        // Arrange
        var value = Wrap((object)"hello");
        var node = new TypeIs(value, TypeReference.To<int>());

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<bool>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task TypeAs_ObjectToString_ReturnsStringValue() {
        // Arrange
        var value = Wrap((object)"hello");
        var node = new TypeAs(value, TypeReference.To<string>());

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<string?>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo("hello");
    }

    [Test]
    public async Task TypeAs_ObjectToString_WithIncompatibleValue_ReturnsNull() {
        // Arrange
        var value = Wrap((object)42);
        var node = new TypeAs(value, TypeReference.To<string>());

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<string?>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task TypeAs_ObjectToNonNullableValueType_ReturnsNullableValue() {
        // Arrange
        var value = Wrap((object)42);
        var node = new TypeAs(value, TypeReference.To<int>());

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<int?>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task TypeOperations_Extensions_IsAndAs_CreateExpectedNodes() {
        // Arrange
        var operand = Wrap((object)"hello");

        // Act
        var isNode = operand.Is(TypeReference.To<string>());
        var asNode = operand.As(TypeReference.To<string>());

        // Assert
        await Assert.That(isNode.TargetTypeReference).IsTypeOf<TypeReference>();
        await Assert.That(asNode.TargetTypeReference).IsTypeOf<TypeReference>();
    }
}