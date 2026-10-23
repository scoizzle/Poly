using Poly.Interpretation;

namespace Poly.Tests.Integration;

/// <summary>
/// Integration tests demonstrating middleware interpreter patterns.
/// Tests the full pipeline with semantic analysis, custom middleware, and code generation.
/// </summary>
public class MiddlewareInterpreterIntegrationTests {
    private static Node Wrap(object? value) => new Constant(value);

    /// <summary>
    /// Test shared context across multiple AST interpretations.
    /// Verifies that a single InterpretationContext can be reused for multiple independent AST fragments
    /// while maintaining consistent parameter bindings.
    /// </summary>
    [Test]
    public async Task SharedContext_ReuseAcrossMultipleFragments_MaintainsConsistentParameterBindings() {
        // Arrange
        var x = new Parameter("x", TypeReference.To<int>());

        // Act - interpret the first AST (x + 10), then a second (x * 2)
        var program1 = Interpreter.Compile(new Add(x, Wrap(10)));
        var program2 = Interpreter.Compile(new Multiply(x, Wrap(2)));

        // Assert - one parameter binding serves both fragments
        using (var exec = Interpreter.Execute(program1, s => s.SetArgs(5)))
            await Assert.That(exec.GetValue<long>()).IsEqualTo(15L);
        using (var exec = Interpreter.Execute(program2, s => s.SetArgs(5)))
            await Assert.That(exec.GetValue<long>()).IsEqualTo(10L);
        using (var exec = Interpreter.Execute(program1, s => s.SetArgs(10)))
            await Assert.That(exec.GetValue<long>()).IsEqualTo(20L);
        using (var exec = Interpreter.Execute(program2, s => s.SetArgs(10)))
            await Assert.That(exec.GetValue<long>()).IsEqualTo(20L);
    }

    /// <summary>
    /// Test that multiple parameters can coexist in the same context.
    /// </summary>
    [Test]
    public async Task SharedContext_MultipleParameters_AllAccessible() {
        // Arrange
        var x = new Parameter("x", TypeReference.To<int>());
        var y = new Parameter("y", TypeReference.To<int>());

        // Act
        var program = Interpreter.Compile(new Add(x, y));

        // Assert
        using (var exec = Interpreter.Execute(program, s => s.SetArgs(3, 7)))
            await Assert.That(exec.GetValue<long>()).IsEqualTo(10L);
        using (var exec = Interpreter.Execute(program, s => s.SetArgs(15, 5)))
            await Assert.That(exec.GetValue<long>()).IsEqualTo(20L);
    }

    /// <summary>
    /// Test constant folding middleware optimization.
    /// Demonstrates custom middleware that optimizes constant expressions before code generation.
    /// </summary>
    [Test]
    public async Task ConstantFoldingMiddleware_SimpleBinary_FoldsToConstantExpression() {
        // Arrange
        var ast = new Add(Wrap(10), Wrap(20));

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(ast));

        // Assert - the constant expression folds to 30
        await Assert.That(exec.GetValue<long>()).IsEqualTo(30L);
    }

    /// <summary>
    /// Test constant folding with nested operations.
    /// Verifies that (10 + 20) + (5 + 3) is properly computed as 38.
    /// </summary>
    [Test]
    public async Task ConstantFoldingMiddleware_NestedOperations_ComputesCorrectly() {
        // Arrange
        var left = new Add(Wrap(10), Wrap(20));   // 30
        var right = new Add(Wrap(5), Wrap(3));    // 8
        var ast = new Add(left, right);           // 30 + 8 = 38

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(ast));

        // Assert
        await Assert.That(exec.GetValue<long>()).IsEqualTo(38L);
    }

    /// <summary>
    /// Test semantic analysis resolves types correctly through the pipeline.
    /// Verifies that complex expressions get proper type information during interpretation.
    /// </summary>
    [Test]
    public async Task SemanticAnalysis_ComplexExpression_ResolvesTypesCorrectly() {
        // Arrange
        var x = new Parameter("x", TypeReference.To<int>());

        // Act - Build and interpret: (x + 10) * 2
        var addTen = new Add(x, Wrap(10));
        var timesTwo = new Multiply(addTen, Wrap(2));

        // Act
        var program = Interpreter.Compile(timesTwo);

        // Assert
        using (var exec = Interpreter.Execute(program, s => s.SetArgs(5)))
            await Assert.That(exec.GetValue<long>()).IsEqualTo(30L);
        using (var exec = Interpreter.Execute(program, s => s.SetArgs(10)))
            await Assert.That(exec.GetValue<long>()).IsEqualTo(40L);
    }

    /// <summary>
    /// Mixed operand types, not a type error: <c>+</c> with an int and a string concatenates, as it
    /// does in C#. The LINQ generator refused this tree, so the assertion could not see the meaning.
    /// </summary>
    [Test]
    public async Task MixedTypes_IntPlusString_ConcatenatesAsCSharpDoes() {
        // Arrange
        var ast = new Add(Wrap(5), Wrap("hello"));

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(ast));

        // Assert
        await Assert.That(exec.GetValue<object>()).IsEqualTo("5hello");
    }

    /// <summary>
    /// Test numeric type promotion in arithmetic operations.
    /// Verifies that the code generator (LinqExpressionGenerator) correctly promotes int + double to double.
    /// </summary>
    [Test]
    public async Task NumericTypePromotion_IntPlusDouble_ProducesCorrectResult() {
        // Arrange
        var ast = new Add(Wrap(10), Wrap(5.5));

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(ast));

        // Assert
        await Assert.That(exec.GetValue<double>()).IsEqualTo(15.5);
    }

    /// <summary>
    /// Test mixed numeric types in multiplication.
    /// Verifies that the code generator promotes int * double to double.
    /// </summary>
    [Test]
    public async Task NumericTypePromotion_IntTimesDouble_ProducesCorrectResult() {
        // Arrange
        var ast = new Multiply(Wrap(10), Wrap(2.5));

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(ast));

        // Assert
        await Assert.That(exec.GetValue<double>()).IsEqualTo(25.0);
    }

    /// <summary>
    /// Test parameter usage in complex nested expression.
    /// </summary>
    [Test]
    public async Task ComplexNested_WithParametersAndConstants_ExecutesCorrectly() {
        // Arrange
        var x = new Parameter("x", TypeReference.To<int>());

        // Act - Build: ((x + 5) * 2) - 3
        var addFive = new Add(x, Wrap(5));
        var timesTwo = new Multiply(addFive, Wrap(2));
        var minusThree = new Subtract(timesTwo, Wrap(3));

        // Act
        var program = Interpreter.Compile(minusThree);

        // Assert
        using (var exec = Interpreter.Execute(program, s => s.SetArgs(0)))
            await Assert.That(exec.GetValue<long>()).IsEqualTo(7L);
        using (var exec = Interpreter.Execute(program, s => s.SetArgs(5)))
            await Assert.That(exec.GetValue<long>()).IsEqualTo(17L);
        using (var exec = Interpreter.Execute(program, s => s.SetArgs(10)))
            await Assert.That(exec.GetValue<long>()).IsEqualTo(27L);
    }

    /// <summary>
    /// Test null coalescing operator with parameters.
    /// </summary>
    [Test]
    public async Task NullCoalescing_WithParameter_ReturnsCorrectValue() {
        // Arrange
        var x = new Parameter("x", TypeReference.To<int?>());

        // Act - Build: x ?? 42
        var ast = new Coalesce(x, Wrap(42));

        // Act
        var program = Interpreter.Compile(ast);

        // Assert
        using (var exec = Interpreter.Execute(program, s => s.SetArgs([null])))
            await Assert.That(exec.GetValue<long>()).IsEqualTo(42L);
        using (var exec = Interpreter.Execute(program, s => s.SetArgs(10)))
            await Assert.That(exec.GetValue<long>()).IsEqualTo(10L);
        using (var exec = Interpreter.Execute(program, s => s.SetArgs(0)))
            await Assert.That(exec.GetValue<long>()).IsEqualTo(0L);
    }

    /// <summary>
    /// Test unary minus operator.
    /// </summary>
    [Test]
    public async Task UnaryMinus_WithPositiveValue_ReturnsNegated() {
        // Act - Build: -42
        var ast = new UnaryMinus(Wrap(42));

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(ast));

        // Assert
        await Assert.That(exec.GetValue<long>()).IsEqualTo(-42L);
    }

    /// <summary>
    /// Test type cast operator.
    /// </summary>
    [Test]
    public async Task TypeCast_IntToDouble_CastsCorrectly() {
        // Act - Build: (double)42
        var ast = new TypeCast(Wrap(42), TypeReference.To<double>());

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(ast));

        // Assert
        await Assert.That(exec.GetValue<double>()).IsEqualTo(42.0);
    }

    /// <summary>
    /// Test that parameter expressions are consistent across multiple calls.
    /// </summary>
    [Test]
    public async Task ContextPreservation_MultipleParameterAccess_UsesSameValue() {
        // Arrange
        var x = new Parameter("x", TypeReference.To<int>());
        var node = new Add(x, x);

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(node), s => s.SetArgs(6));

        // Assert - both references read the one argument
        await Assert.That(exec.GetValue<long>()).IsEqualTo(12L);
    }

    /// <summary>
    /// Test block expression with multiple statements.
    /// </summary>
    [Test]
    public async Task Block_MultipleStatements_ExecutesAllAndReturnsLast() {
        // Act - Build: { 10; 20; 30 } -> evaluates to 30
        var ast = new Block(
            Wrap(10),
            Wrap(20),
            Wrap(30));

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(ast));

        // Assert
        await Assert.That(exec.GetValue<long>()).IsEqualTo(30L);
    }

    /// <summary>
    /// Test modulo operator.
    /// </summary>
    [Test]
    public async Task Modulo_TenModThree_ReturnsOne() {
        // Act - Build: 10 % 3
        var ast = new Modulo(Wrap(10), Wrap(3));

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(ast));

        // Assert
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }
}