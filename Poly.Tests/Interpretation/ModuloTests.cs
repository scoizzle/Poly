using System.Linq.Expressions;
using Poly.Interpretation;
using Poly.Interpretation.Expressions;
using Poly.Interpretation.LinqInterpreter;

namespace Poly.Tests.Interpretation;

public class ModuloTests {
    [Test]
    public async Task Modulo_WithIntegers_ReturnsRemainder()
    {
        // Arrange
        var builder = new LinqExecutionPlanBuilder();
        var left = new Constant(10);
        var right = new Constant(3);
        var modulo = new BinaryOperation(BinaryOperationKind.Modulo, left, right);

        // Act
        var expression = modulo.Evaluate(builder);
        var lambda = Expression.Lambda<Func<int>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(1);
    }

    [Test]
    public async Task Modulo_WithExactDivision_ReturnsZero()
    {
        // Arrange
        var builder = new LinqExecutionPlanBuilder();
        var left = new Constant(15);
        var right = new Constant(5);
        var modulo = new BinaryOperation(BinaryOperationKind.Modulo, left, right);

        // Act
        var expression = modulo.Evaluate(builder);
        var lambda = Expression.Lambda<Func<int>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(0);
    }

    [Test]
    public async Task Modulo_WithDoubles_ReturnsRemainder()
    {
        // Arrange
        var builder = new LinqExecutionPlanBuilder();
        var left = new Constant(10.5);
        var right = new Constant(3.0);
        var modulo = new BinaryOperation(BinaryOperationKind.Modulo, left, right);

        // Act
        var expression = modulo.Evaluate(builder);
        var lambda = Expression.Lambda<Func<double>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(1.5);
    }

    [Test]
    public async Task Modulo_WithParameters_EvaluatesCorrectly()
    {
        // Arrange
        var builder = new LinqExecutionPlanBuilder();
        var typeDef = builder.GetTypeDefinition(typeof(int).FullName!);
        var param1 = builder.Parameter("a", typeDef);
        var param2 = builder.Parameter("b", typeDef);
        var paramRef1 = new NamedReference("a");
        var paramRef2 = new NamedReference("b");
        var modulo = new BinaryOperation(BinaryOperationKind.Modulo, paramRef1, paramRef2);

        // Act
        var expression = modulo.Evaluate(builder);
        var lambda = Expression.Lambda<Func<int, int, int>>(expression, param1, param2);
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(17, 5)).IsEqualTo(2);
        await Assert.That(compiled(100, 7)).IsEqualTo(2);
        await Assert.That(compiled(8, 4)).IsEqualTo(0);
    }

    [Test]
    public async Task Modulo_WithNegativeNumbers_HandlesCorrectly()
    {
        // Arrange
        var builder = new LinqExecutionPlanBuilder();
        var left = new Constant(-10);
        var right = new Constant(3);
        var modulo = new BinaryOperation(BinaryOperationKind.Modulo, left, right);

        // Act
        var expression = modulo.Evaluate(builder);
        var lambda = Expression.Lambda<Func<int>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert: C# modulo preserves sign of dividend
        await Assert.That(result).IsEqualTo(-1);
    }

    [Test]
    public async Task Modulo_WithComplexExpression_EvaluatesCorrectly()
    {
        // Arrange
        var builder = new LinqExecutionPlanBuilder();
        var typeDef = builder.GetTypeDefinition(typeof(int).FullName!);
        var param = builder.Parameter("x", typeDef);
        var paramRef = new NamedReference("x");

        // (x * 2) % 5
        var multiply = new BinaryOperation(BinaryOperationKind.Multiply, paramRef, new Constant(2));
        var modulo = new BinaryOperation(BinaryOperationKind.Modulo, multiply, new Constant(5));

        // Act
        var expression = modulo.Evaluate(builder);
        var lambda = Expression.Lambda<Func<int, int>>(expression, param);
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(7)).IsEqualTo(4);  // (7 * 2) % 5 = 14 % 5 = 4
        await Assert.That(compiled(3)).IsEqualTo(1);  // (3 * 2) % 5 = 6 % 5 = 1
    }
}
