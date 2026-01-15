using System.Linq.Expressions;

using Poly.Interpretation;

namespace Poly.Tests.Interpretation;

public class FluentValueApiTests {
    [Test]
    public async Task FluentApi_ArithmeticChaining_WorksCorrectly()
    {
        // Arrange
        var context = new InterpretationContext();
        var param = context.AddParameter<int>("x");

        // x + 5 - 2 * 3
        var expr = param.Add(Value.Wrap(5)).Subtract(Value.Wrap(2)).Multiply(Value.Wrap(3));

        // Act
        var expression = expr.BuildExpression(context);
        var lambda = Expression.Lambda<Func<int, int>>(expression, param.BuildExpression(context));
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(10)).IsEqualTo(39); // (10 + 5 - 2) * 3 = 39
    }

    [Test]
    public async Task FluentApi_ComparisonChaining_WorksCorrectly()
    {
        // Arrange
        var context = new InterpretationContext();
        var param = context.AddParameter<int>("x");

        // x > 10
        var expr = param.GreaterThan(Value.Wrap(10));

        // Act
        var expression = expr.BuildExpression(context);
        var lambda = Expression.Lambda<Func<int, bool>>(expression, param.BuildExpression(context));
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(15)).IsTrue();
        await Assert.That(compiled(5)).IsFalse();
    }

    [Test]
    public async Task FluentApi_BooleanChaining_WorksCorrectly()
    {
        // Arrange
        var context = new InterpretationContext();
        var x = context.AddParameter<int>("x");
        var y = context.AddParameter<int>("y");

        // x > 10 && y < 20
        var expr = x.GreaterThan(Value.Wrap(10)).And(y.LessThan(Value.Wrap(20)));

        // Act
        var expression = expr.BuildExpression(context);
        var lambda = Expression.Lambda<Func<int, int, bool>>(
            expression,
            x.BuildExpression(context),
            y.BuildExpression(context)
        );
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(15, 10)).IsTrue();
        await Assert.That(compiled(5, 10)).IsFalse();
        await Assert.That(compiled(15, 25)).IsFalse();
    }

    [Test]
    public async Task FluentApi_ConditionalExpression_WorksCorrectly()
    {
        // Arrange
        var context = new InterpretationContext();
        var param = context.AddParameter<int>("x");

        // x > 10 ? x * 2 : x + 5
        var expr = param.GreaterThan(Value.Wrap(10))
            .Conditional(param.Multiply(Value.Wrap(2)), param.Add(Value.Wrap(5)));

        // Act
        var expression = expr.BuildExpression(context);
        var lambda = Expression.Lambda<Func<int, int>>(expression, param.BuildExpression(context));
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(15)).IsEqualTo(30); // 15 * 2
        await Assert.That(compiled(5)).IsEqualTo(10); // 5 + 5
    }

    [Test]
    public async Task FluentApi_CoalesceExpression_WorksCorrectly()
    {
        // Arrange
        var context = new InterpretationContext();
        var param = context.AddParameter<int?>("x");

        // x ?? 42
        var expr = param.Coalesce(Value.Wrap(42));

        // Act
        var expression = expr.BuildExpression(context);
        var lambda = Expression.Lambda<Func<int?, int>>(expression, param.BuildExpression(context));
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(10)).IsEqualTo(10);
        await Assert.That(compiled(null)).IsEqualTo(42);
    }

    [Test]
    public async Task FluentApi_NegateOperation_WorksCorrectly()
    {
        // Arrange
        var context = new InterpretationContext();
        var param = context.AddParameter<int>("x");

        // -x + 10
        var expr = param.Negate().Add(Value.Wrap(10));

        // Act
        var expression = expr.BuildExpression(context);
        var lambda = Expression.Lambda<Func<int, int>>(expression, param.BuildExpression(context));
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(5)).IsEqualTo(5); // -5 + 10 = 5
        await Assert.That(compiled(-3)).IsEqualTo(13); // -(-3) + 10 = 13
    }

    [Test]
    public async Task FluentApi_NotOperation_WorksCorrectly()
    {
        // Arrange
        var context = new InterpretationContext();
        var param = context.AddParameter<bool>("x");

        // !x
        var expr = param.Not();

        // Act
        var expression = expr.BuildExpression(context);
        var lambda = Expression.Lambda<Func<bool, bool>>(expression, param.BuildExpression(context));
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(true)).IsFalse();
        await Assert.That(compiled(false)).IsTrue();
    }

    [Test]
    public async Task FluentApi_TypeCastOperation_WorksCorrectly()
    {
        // Arrange
        var context = new InterpretationContext();
        var param = context.AddParameter<int>("x");
        var doubleType = context.GetTypeDefinition<double>()!;

        // (double)x + 0.5
        var expr = param.CastTo(doubleType).Add(Value.Wrap(0.5));

        // Act
        var expression = expr.BuildExpression(context);
        var lambda = Expression.Lambda<Func<int, double>>(expression, param.BuildExpression(context));
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(10)).IsEqualTo(10.5);
    }

    [Test]
    public async Task FluentApi_IndexAccess_WorksCorrectly()
    {
        // Arrange
        var context = new InterpretationContext();
        var param = context.AddParameter<List<int>>("list");

        // list[0]
        var expr = param.Index(Value.Wrap(0));

        // Act
        var expression = expr.BuildExpression(context);
        var lambda = Expression.Lambda<Func<List<int>, int>>(expression, param.BuildExpression(context));
        var compiled = lambda.Compile();

        // Assert
        var testList = new List<int> { 10, 20, 30 };
        await Assert.That(compiled(testList)).IsEqualTo(10);
    }

    [Test]
    public async Task FluentApi_MemberAccess_WorksCorrectly()
    {
        // Arrange
        var context = new InterpretationContext();
        var param = context.AddParameter<string>("str");

        // str.Length
        var expr = param.GetMember("Length");

        // Act
        var expression = expr.BuildExpression(context);
        var lambda = Expression.Lambda<Func<string, int>>(expression, param.BuildExpression(context));
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled("hello")).IsEqualTo(5);
        await Assert.That(compiled("test")).IsEqualTo(4);
    }

    [Test]
    public async Task FluentApi_ComplexExpression_WorksCorrectly()
    {
        // Arrange
        var context = new InterpretationContext();
        var x = context.AddParameter<int>("x");
        var y = context.AddParameter<int>("y");

        // Complex: (x + y) > 100 ? (x * y) : (x - y)
        var sum = x.Add(y);
        var condition = sum.GreaterThan(Value.Wrap(100));
        var product = x.Multiply(y);
        var difference = x.Subtract(y);
        var expr = condition.Conditional(product, difference);

        // Act
        var expression = expr.BuildExpression(context);
        var lambda = Expression.Lambda<Func<int, int, int>>(
            expression,
            x.BuildExpression(context),
            y.BuildExpression(context)
        );
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(60, 50)).IsEqualTo(3000); // 60 + 50 = 110 > 100, so 60 * 50 = 3000
        await Assert.That(compiled(30, 20)).IsEqualTo(10); // 30 + 20 = 50 < 100, so 30 - 20 = 10
    }
}