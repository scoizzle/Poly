using Poly.Interpretation;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Integration tests for the Interpreter with complex multi-node expressions.
/// Tests the full middleware pipeline including semantic analysis and LINQ compilation.
/// </summary>
public class InterpreterIntegrationTests {
    [Test]
    public async Task ComplexExpression_MultipleNestedOperations_EvaluatesCorrectly() {
        // Arrange - ((10 + 5) * 2) - (8 / 2) + 1 = 30 - 4 + 1 = 27
        var add = new Add(new Constant(10), new Constant(5));           // 15
        var multiply = new Multiply(add, new Constant(2));              // 30
        var divide = new Divide(new Constant(8), new Constant(2));      // 4
        var subtract = new Subtract(multiply, divide);                  // 26
        var node = new Add(subtract, new Constant(1));                  // 27

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<int>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsEqualTo(27);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<int>()).IsEqualTo(27);
    }

    [Test]
    public async Task ParameterizedExpression_SingleParameter_EvaluatesForMultipleInputs() {
        // Arrange - (x * 2) + 10
        var param = new Parameter("x", TypeReference.To<int>());
        var multiply = new Multiply(param, new Constant(2));
        var node = new Add(multiply, new Constant(10));
        var compiled = node.CompileLambda<Func<int, int>>((param, typeof(int)));

        // Assert
        await Assert.That(compiled(5)).IsEqualTo(20);    // (5 * 2) + 10 = 20
        await Assert.That(compiled(0)).IsEqualTo(10);    // (0 * 2) + 10 = 10
        await Assert.That(compiled(-3)).IsEqualTo(4);    // (-3 * 2) + 10 = 4
        var program = Interpreter.Compile(node);
        using var exec5 = Interpreter.Execute(program, s => s.SetArgs(5));
        await Assert.That(exec5.GetValue<int>()).IsEqualTo(20);
        using var exec0 = Interpreter.Execute(program, s => s.SetArgs(0));
        await Assert.That(exec0.GetValue<int>()).IsEqualTo(10);
        using var execNeg = Interpreter.Execute(program, s => s.SetArgs(-3));
        await Assert.That(execNeg.GetValue<int>()).IsEqualTo(4);
    }

    [Test]
    public async Task ParameterizedExpression_MultipleParameters_EvaluatesCorrectly() {
        // Arrange - (x + y) * z
        var x = new Parameter("x", TypeReference.To<int>());
        var y = new Parameter("y", TypeReference.To<int>());
        var z = new Parameter("z", TypeReference.To<int>());
        var add = new Add(x, y);
        var node = new Multiply(add, z);
        var compiled = node.CompileLambda<Func<int, int, int, int>>((x, typeof(int)), (y, typeof(int)), (z, typeof(int)));

        // Assert
        await Assert.That(compiled(2, 3, 4)).IsEqualTo(20);   // (2 + 3) * 4 = 20
        await Assert.That(compiled(5, 5, 2)).IsEqualTo(20);   // (5 + 5) * 2 = 20
        await Assert.That(compiled(1, 1, 10)).IsEqualTo(20);  // (1 + 1) * 10 = 20
        var program = Interpreter.Compile(node);
        using var exec1 = Interpreter.Execute(program, s => s.SetArgs(2, 3, 4));
        await Assert.That(exec1.GetValue<int>()).IsEqualTo(20);
        using var exec2 = Interpreter.Execute(program, s => s.SetArgs(5, 5, 2));
        await Assert.That(exec2.GetValue<int>()).IsEqualTo(20);
        using var exec3 = Interpreter.Execute(program, s => s.SetArgs(1, 1, 10));
        await Assert.That(exec3.GetValue<int>()).IsEqualTo(20);
    }

    [Test]
    public async Task ConditionalWithArithmetic_EvaluatesCorrectBranch() {
        // Arrange - if (x > 10) then (x * 2) else (x + 5)
        var param = new Parameter("x", TypeReference.To<int>());
        var condition = new GreaterThan(param, new Constant(10));
        var ifTrue = new Multiply(param, new Constant(2));
        var ifFalse = new Add(param, new Constant(5));
        var node = new Conditional(condition, ifTrue, ifFalse);
        var compiled = node.CompileLambda<Func<int, int>>((param, typeof(int)));

        // Assert
        await Assert.That(compiled(15)).IsEqualTo(30);    // 15 > 10: true -> 15 * 2 = 30
        await Assert.That(compiled(5)).IsEqualTo(10);     // 5 > 10: false -> 5 + 5 = 10
        await Assert.That(compiled(10)).IsEqualTo(15);    // 10 > 10: false -> 10 + 5 = 15
        var program = Interpreter.Compile(node);
        using var exec15 = Interpreter.Execute(program, s => s.SetArgs(15));
        await Assert.That(exec15.GetValue<int>()).IsEqualTo(30);
        using var exec5 = Interpreter.Execute(program, s => s.SetArgs(5));
        await Assert.That(exec5.GetValue<int>()).IsEqualTo(10);
        using var exec10 = Interpreter.Execute(program, s => s.SetArgs(10));
        await Assert.That(exec10.GetValue<int>()).IsEqualTo(15);
    }

    [Test]
    public async Task TypePromotion_IntAndDouble_PromotesCorrectly() {
        // Arrange - (int + double) * int should promote to double
        var node = new Multiply(
            new Add(new Constant(5), new Constant(2.5)),
            new Constant(2));

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<double>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsEqualTo(15.0);    // (5 + 2.5) * 2 = 15.0
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<double>()).IsEqualTo(15.0);
    }

    [Test]
    public async Task NullCoalesceInExpression_HandlesNullCorrectly() {
        // Arrange - (x ?? 10) + 5
        var param = new Parameter("x", TypeReference.To<int?>());
        var coalesce = new Coalesce(param, new Constant(10));
        var node = new Add(coalesce, new Constant(5));

        // Act
        var compiled = node.CompileLambda<Func<int?, int>>((param, typeof(int?)));

        // Assert
        await Assert.That(compiled(null)).IsEqualTo(15);    // (null ?? 10) + 5 = 15
        await Assert.That(compiled(20)).IsEqualTo(25);      // (20 ?? 10) + 5 = 25
        await Assert.That(compiled(0)).IsEqualTo(5);        // (0 ?? 10) + 5 = 5
        var program = Interpreter.Compile(node);
        using var execNull = Interpreter.Execute(program, s => s.SetArgs([null]));
        await Assert.That(execNull.GetValue<int>()).IsEqualTo(15);
        using var exec20 = Interpreter.Execute(program, s => s.SetArgs(20));
        await Assert.That(exec20.GetValue<int>()).IsEqualTo(25);
    }

    [Test]
    public async Task UnaryMinusInComplexExpression_NegatesCorrectly() {
        // Arrange - (10 - (-5)) * 2 = 30
        var negated = new UnaryMinus(new Constant(5));
        var subtract = new Subtract(new Constant(10), negated);
        var node = new Multiply(subtract, new Constant(2));

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<int>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsEqualTo(30);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<int>()).IsEqualTo(30);
    }

    [Test]
    public async Task NestedConditionals_EvaluatesCorrectPath() {
        // Arrange - if (x > 10) then (if (x > 20) then 100 else 50) else 0
        var param = new Parameter("x", TypeReference.To<int>());
        var innerCondition = new GreaterThan(param, new Constant(20));
        var innerConditional = new Conditional(innerCondition, new Constant(100), new Constant(50));
        var outerCondition = new GreaterThan(param, new Constant(10));
        var node = new Conditional(outerCondition, innerConditional, new Constant(0));

        // Act
        var compiled = node.CompileLambda<Func<int, int>>((param, typeof(int)));

        // Assert
        await Assert.That(compiled(25)).IsEqualTo(100);    // 25 > 10 && 25 > 20
        await Assert.That(compiled(15)).IsEqualTo(50);     // 15 > 10 && 15 <= 20
        await Assert.That(compiled(5)).IsEqualTo(0);       // 5 <= 10
        var program = Interpreter.Compile(node);
        using var exec25 = Interpreter.Execute(program, s => s.SetArgs(25));
        await Assert.That(exec25.GetValue<int>()).IsEqualTo(100);
        using var exec15 = Interpreter.Execute(program, s => s.SetArgs(15));
        await Assert.That(exec15.GetValue<int>()).IsEqualTo(50);
        using var exec5 = Interpreter.Execute(program, s => s.SetArgs(5));
        await Assert.That(exec5.GetValue<int>()).IsEqualTo(0);
    }

    [Test]
    public async Task BlockExpression_ReturnsLastValue() {
        // Arrange - { 10; 20; 30 } should return 30
        var node = new Block(
            new Constant(10),
            new Constant(20),
            new Constant(30));

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<int>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsEqualTo(30);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<int>()).IsEqualTo(30);
    }

    [Test]
    public async Task BlockWithArithmetic_EvaluatesAllAndReturnsLast() {
        // Arrange - { 5 + 3; 10 * 2; 100 / 4 } should return 25
        var node = new Block(
            new Add(new Constant(5), new Constant(3)),
            new Multiply(new Constant(10), new Constant(2)),
            new Divide(new Constant(100), new Constant(4)));

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<int>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsEqualTo(25);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<int>()).IsEqualTo(25);
    }

    [Test]
    public async Task MathematicalFormula_QuadraticFormulaPart_EvaluatesCorrectly() {
        // Arrange - (-b + sqrt(b^2 - 4ac)) / (2a) simplified part: b^2 - 4ac
        // Let b = 5, a = 1, c = 6
        // Result should be: 5^2 - 4*1*6 = 25 - 24 = 1
        var b = new Parameter("b", TypeReference.To<int>());
        var a = new Parameter("a", TypeReference.To<int>());
        var c = new Parameter("c", TypeReference.To<int>());

        var bSquared = new Multiply(b, b);
        var fourA = new Multiply(new Constant(4), a);
        var fourAC = new Multiply(fourA, c);
        var node = new Subtract(bSquared, fourAC);

        // Act
        var compiled = node.CompileLambda<Func<int, int, int, int>>((b, typeof(int)), (a, typeof(int)), (c, typeof(int)));

        // Assert
        await Assert.That(compiled(5, 1, 6)).IsEqualTo(1);     // 25 - 24 = 1
        await Assert.That(compiled(4, 1, 3)).IsEqualTo(4);     // 16 - 12 = 4
        await Assert.That(compiled(10, 2, 5)).IsEqualTo(60);   // 100 - 40 = 60
        var program = Interpreter.Compile(node);
        using var exec1 = Interpreter.Execute(program, s => s.SetArgs(5, 1, 6));
        await Assert.That(exec1.GetValue<int>()).IsEqualTo(1);
        using var exec2 = Interpreter.Execute(program, s => s.SetArgs(4, 1, 3));
        await Assert.That(exec2.GetValue<int>()).IsEqualTo(4);
        using var exec3 = Interpreter.Execute(program, s => s.SetArgs(10, 2, 5));
        await Assert.That(exec3.GetValue<int>()).IsEqualTo(60);
    }

    [Test]
    public async Task StringConcatenation_AddingStrings_ConcatenatesCorrectly() {
        // Arrange
        var node = new Add(new Constant("Hello "), new Constant("World"));

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<string>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsEqualTo("Hello World");
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<string>()).IsEqualTo("Hello World");
    }

    [Test]
    public async Task StringParameterExpression_ConcatenatesWithParameter() {
        // Arrange
        var param = new Parameter("name", TypeReference.To<string>());
        var node = new Add(new Constant("Hello, "), param);

        // Act
        var compiled = node.CompileLambda<Func<string, string>>((param, typeof(string)));

        // Assert
        await Assert.That(compiled("Alice")).IsEqualTo("Hello, Alice");
        await Assert.That(compiled("Bob")).IsEqualTo("Hello, Bob");
        var program = Interpreter.Compile(node);
        using var execA = Interpreter.Execute(program, s => s.SetArgs("Alice"));
        await Assert.That(execA.GetValue<string>()).IsEqualTo("Hello, Alice");
        using var execB = Interpreter.Execute(program, s => s.SetArgs("Bob"));
        await Assert.That(execB.GetValue<string>()).IsEqualTo("Hello, Bob");
    }
}