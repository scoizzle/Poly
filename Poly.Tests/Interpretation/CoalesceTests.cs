using System.Linq.Expressions;
using Poly.Interpretation;
using Poly.Interpretation.Expressions;
using Poly.Interpretation.LinqInterpreter;

namespace Poly.Tests.Interpretation;

public class CoalesceTests {
    [Test]
    public async Task Coalesce_WithNullLeft_ReturnsRightValue()
    {
        // Arrange
        var builder = new LinqExecutionPlanBuilder();
        var typeDef = builder.GetTypeDefinition(typeof(int?).FullName!);
        var param = builder.Parameter("nullable", typeDef);
        var paramRef = new NamedReference("nullable");
        
        var rightValue = new Constant(42);
        var coalesce = new BinaryOperation(BinaryOperationKind.Coalesce, paramRef, rightValue);

        // Act
        var expression = coalesce.Evaluate(builder);
        var lambda = Expression.Lambda<Func<int?, int>>(expression, param);
        var compiled = lambda.Compile();
        var result = compiled(null);

        // Assert
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task Coalesce_WithNonNullLeft_ReturnsLeftValue()
    {
        // Arrange
        var builder = new LinqExecutionPlanBuilder();
        var typeDef = builder.GetTypeDefinition(typeof(int?).FullName!);
        var param = builder.Parameter("nullable", typeDef);
        var paramRef = new NamedReference("nullable");
        
        var fallback = new Constant(42);
        var coalesce = new BinaryOperation(BinaryOperationKind.Coalesce, paramRef, fallback);

        // Act
        var expression = coalesce.Evaluate(builder);
        var lambda = Expression.Lambda<Func<int?, int>>(expression, param);
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(99)).IsEqualTo(99);
        await Assert.That(compiled(null)).IsEqualTo(42);
    }

    [Test]
    public async Task Coalesce_WithStringParameter_EvaluatesCorrectly()
    {
        // Arrange
        var builder = new LinqExecutionPlanBuilder();
        var typeDef = builder.GetTypeDefinition(typeof(string).FullName!);
        var param = builder.Parameter("input", typeDef);
        var paramRef = new NamedReference("input");

        // input ?? "default"
        var coalesce = new BinaryOperation(BinaryOperationKind.Coalesce, paramRef, new Constant("default"));

        // Act
        var expression = coalesce.Evaluate(builder);
        var lambda = Expression.Lambda<Func<string?, string>>(expression, param);
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled("hello")).IsEqualTo("hello");
        await Assert.That(compiled(null)).IsEqualTo("default");
    }

    [Test]
    public async Task Coalesce_ChainedOperators_WorksCorrectly()
    {
        // Arrange
        var builder = new LinqExecutionPlanBuilder();
        var typeDef = builder.GetTypeDefinition(typeof(string).FullName!);
        var param1 = builder.Parameter("first", typeDef);
        var param2 = builder.Parameter("second", typeDef);
        var paramRef1 = new NamedReference("first");
        var paramRef2 = new NamedReference("second");

        // first ?? second ?? "fallback"
        var innerCoalesce = new BinaryOperation(BinaryOperationKind.Coalesce, paramRef1, paramRef2);
        var outerCoalesce = new BinaryOperation(BinaryOperationKind.Coalesce, innerCoalesce, new Constant("fallback"));

        // Act
        var expression = outerCoalesce.Evaluate(builder);
        var lambda = Expression.Lambda<Func<string?, string?, string>>(expression, param1, param2);
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled("A", "B")).IsEqualTo("A");
        await Assert.That(compiled(null, "B")).IsEqualTo("B");
        await Assert.That(compiled(null, null)).IsEqualTo("fallback");
    }

    [Test]
    public async Task Coalesce_WithConstantNull_ReturnsRightValue()
    {
        // Arrange
        var builder = new LinqExecutionPlanBuilder();
        var leftValue = new Constant(null);
        var rightValue = new Constant(42);
        var coalesce = new BinaryOperation(BinaryOperationKind.Coalesce, leftValue, rightValue);

        // Act
        var expression = coalesce.Evaluate(builder);
        var lambda = Expression.Lambda<Func<int>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task Coalesce_WithBothNonNull_ReturnsLeftValue()
    {
        // Arrange
        var builder = new LinqExecutionPlanBuilder();
        var leftValue = new Constant(10);
        var rightValue = new Constant(20);
        var coalesce = new BinaryOperation(BinaryOperationKind.Coalesce, leftValue, rightValue);

        // Act
        var expression = coalesce.Evaluate(builder);
        var lambda = Expression.Lambda<Func<int>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(10);
    }
}
