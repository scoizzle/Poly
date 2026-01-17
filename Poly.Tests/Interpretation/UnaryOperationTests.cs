using System.Linq.Expressions;
using Poly.Interpretation;
using Poly.Interpretation.Expressions;
using Poly.Interpretation.LinqInterpreter;

namespace Poly.Tests.Interpretation;

public class UnaryOperationTests {
    [Test]
    public async Task Negate_WithPositiveInteger_ReturnsNegative()
    {
        // Arrange
        var builder = new LinqExecutionPlanBuilder();
        var operand = new Constant(42);
        var negate = new UnaryOperation(UnaryOperationKind.Negate, operand);

        // Act
        var expression = negate.Evaluate(builder);
        var lambda = Expression.Lambda<Func<int>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(-42);
    }

    [Test]
    public async Task Negate_WithNegativeInteger_ReturnsPositive()
    {
        // Arrange
        var builder = new LinqExecutionPlanBuilder();
        var operand = new Constant(-99);
        var negate = new UnaryOperation(UnaryOperationKind.Negate, operand);

        // Act
        var expression = negate.Evaluate(builder);
        var lambda = Expression.Lambda<Func<int>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(99);
    }

    [Test]
    public async Task Negate_WithZero_ReturnsZero()
    {
        // Arrange
        var builder = new LinqExecutionPlanBuilder();
        var operand = new Constant(0);
        var negate = new UnaryOperation(UnaryOperationKind.Negate, operand);

        // Act
        var expression = negate.Evaluate(builder);
        var lambda = Expression.Lambda<Func<int>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(0);
    }

    [Test]
    public async Task Negate_WithDouble_NegatesCorrectly()
    {
        // Arrange
        var builder = new LinqExecutionPlanBuilder();
        var operand = new Constant(3.14);
        var negate = new UnaryOperation(UnaryOperationKind.Negate, operand);

        // Act
        var expression = negate.Evaluate(builder);
        var lambda = Expression.Lambda<Func<double>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(-3.14);
    }

    [Test]
    public async Task Negate_DoubleNegation_ReturnsOriginalValue()
    {
        // Arrange
        var builder = new LinqExecutionPlanBuilder();
        var typeDef = builder.GetTypeDefinition(typeof(int).FullName!);
        var param = builder.Parameter("x", typeDef);
        var paramRef = new NamedReference("x");
        
        var innerNegate = new UnaryOperation(UnaryOperationKind.Negate, paramRef);
        var outerNegate = new UnaryOperation(UnaryOperationKind.Negate, innerNegate);

        // Act
        var expression = outerNegate.Evaluate(builder);
        var lambda = Expression.Lambda<Func<int, int>>(expression, param);
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(42)).IsEqualTo(42);
        await Assert.That(compiled(-7)).IsEqualTo(-7);
    }

    [Test]
    public async Task Not_WithTrue_ReturnsFalse()
    {
        // Arrange
        var builder = new LinqExecutionPlanBuilder();
        var operand = new Constant(true);
        var not = new UnaryOperation(UnaryOperationKind.Not, operand);

        // Act
        var expression = not.Evaluate(builder);
        var lambda = Expression.Lambda<Func<bool>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task Not_WithFalse_ReturnsTrue()
    {
        // Arrange
        var builder = new LinqExecutionPlanBuilder();
        var operand = new Constant(false);
        var not = new UnaryOperation(UnaryOperationKind.Not, operand);

        // Act
        var expression = not.Evaluate(builder);
        var lambda = Expression.Lambda<Func<bool>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Not_DoubleNegation_ReturnsOriginalValue()
    {
        // Arrange
        var builder = new LinqExecutionPlanBuilder();
        var typeDef = builder.GetTypeDefinition(typeof(bool).FullName!);
        var param = builder.Parameter("flag", typeDef);
        var paramRef = new NamedReference("flag");
        
        var innerNot = new UnaryOperation(UnaryOperationKind.Not, paramRef);
        var outerNot = new UnaryOperation(UnaryOperationKind.Not, innerNot);

        // Act
        var expression = outerNot.Evaluate(builder);
        var lambda = Expression.Lambda<Func<bool, bool>>(expression, param);
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(true)).IsTrue();
        await Assert.That(compiled(false)).IsFalse();
    }

    [Test]
    public async Task Negate_WithNullArgument_ThrowsArgumentNullException()
    {
        // Assert
        await Assert.That(() => new UnaryOperation(UnaryOperationKind.Negate, null!))
            .Throws<ArgumentNullException>();
    }
}
