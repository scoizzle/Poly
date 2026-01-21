using Poly.Tests.TestHelpers;
using System.Linq.Expressions;

using Poly.Interpretation;
using Expr = System.Linq.Expressions.Expression;
using Poly.Interpretation.AbstractSyntaxTree;

namespace Poly.Tests.Interpretation;

public class CoalesceTests {
    [Test]
    public async Task Coalesce_WithNullLeft_ReturnsRightValue()
    {
        // Arrange
        var context = new InterpretationContext();
        var param = context.AddParameter<int?>("nullable");
        var rightValue = Wrap(42);
        var coalesce = new Coalesce(param, rightValue);

        // Act
        var expression = coalesce.BuildExpression(context);
        var lambda = Expr.Lambda<Func<int?, int>>(expression, param.GetParameterExpression(context));
        var compiled = lambda.Compile();
        var result = compiled(null);

        // Assert
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task Coalesce_WithNonNullLeft_ReturnsLeftValue()
    {
        // Arrange
        var context = new InterpretationContext();
        var param = context.AddParameter<int?>("nullable");
        var fallback = Wrap(42);
        var coalesce = new Coalesce(param, fallback);

        // Act
        var expression = coalesce.BuildExpression(context);
        var lambda = Expr.Lambda<Func<int?, int>>(expression, param.GetParameterExpression(context));
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(99)).IsEqualTo(99);
        await Assert.That(compiled(null)).IsEqualTo(42);
    }

    [Test]
    public async Task Coalesce_WithParameterLeft_EvaluatesCorrectly()
    {
        // Arrange
        var context = new InterpretationContext();
        var stringTypeDef = context.GetTypeDefinition<string>();
        var param = context.AddParameter<string?>("input");

        // input ?? "default"
        var coalesce = new Coalesce(param, Wrap("default"));

        // Act
        var expression = coalesce.BuildExpression(context);
        var lambda = Expr.Lambda<Func<string?, string>>(expression, param.GetParameterExpression(context));
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled("hello")).IsEqualTo("hello");
        await Assert.That(compiled(null)).IsEqualTo("default");
    }

    [Test]
    public async Task Coalesce_ChainedOperators_WorksCorrectly()
    {
        // Arrange
        var context = new InterpretationContext();
        var param1 = context.AddParameter<string?>("first");
        var param2 = context.AddParameter<string?>("second");

        // first ?? second ?? "fallback"
        var innerCoalesce = new Coalesce(param1, param2);
        var outerCoalesce = new Coalesce(innerCoalesce, Wrap("fallback"));

        // Act
        var expression = outerCoalesce.BuildExpression(context);
        var lambda = Expr.Lambda<Func<string?, string?, string>>(
            expression,
            param1.GetParameterExpression(context),
            param2.GetParameterExpression(context)
        );
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled("A", "B")).IsEqualTo("A");
        await Assert.That(compiled(null, "B")).IsEqualTo("B");
        await Assert.That(compiled(null, null)).IsEqualTo("fallback");
    }

    [Test]
    public async Task Coalesce_WithObjects_WorksCorrectly()
    {
        // Arrange
        var context = new InterpretationContext();
        var objectTypeDef = context.GetTypeDefinition<object>();
        var param = context.AddParameter<object?>("obj");

        var fallback = new { Value = 42 };
        var coalesce = new Coalesce(param, Wrap(fallback));

        // Act
        var expression = coalesce.BuildExpression(context);
        var lambda = Expr.Lambda<Func<object?, object>>(expression, param.GetParameterExpression(context));
        var compiled = lambda.Compile();

        // Assert
        var testObj = new { Value = 99 };
        await Assert.That(compiled(testObj)).IsEqualTo(testObj);
        await Assert.That(compiled(null)).IsEqualTo(fallback);
    }

    [Test]
    public async Task Coalesce_GetTypeDefinition_ReturnsRightHandType()
    {
        // Arrange
        var context = new InterpretationContext();
        var leftValue = Wrap(null);
        var rightValue = Wrap(42);
        var coalesce = new Coalesce(leftValue, rightValue);

        // Act
        var typeDef = coalesce.GetResolvedType(context);

        // Assert
        await Assert.That(typeDef).IsNotNull();
        await Assert.That(typeDef.ReflectedType).IsEqualTo(typeof(int));
    }

    [Test]
    public async Task Coalesce_ToString_ReturnsExpectedFormat()
    {
        // Arrange
        var leftValue = Null;
        var rightValue = Wrap(42);
        var coalesce = new Coalesce(leftValue, rightValue);

        // Act
        var result = coalesce.ToString();

        // Assert
        await Assert.That(result).IsEqualTo("(null ?? 42)");
    }

    [Test]
    public async Task Coalesce_WithNullArguments_AllowsNulls()
    {
        // Act
        var coalesceLeftNull = new Coalesce(null!, Wrap(1));
        var coalesceRightNull = new Coalesce(Null, null!);

        // Assert
        await Assert.That(coalesceLeftNull).IsNotNull();
        await Assert.That(coalesceRightNull).IsNotNull();
    }
}