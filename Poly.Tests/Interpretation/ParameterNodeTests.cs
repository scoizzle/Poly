using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Interpretation.AbstractSyntaxTree.Arithmetic;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Unit tests for Parameter AST nodes and their LINQ expression compilation.
/// </summary>
public class ParameterNodeTests
{
    [Test]
    public async Task Parameter_IntType_CompilesAndExecutesWithValue()
    {
        // Arrange
        var param = new Parameter("x", TypeReference.To<int>());

        // Act
        var compiled = param.CompileLambda<Func<int, int>>((param, typeof(int)));

        // Assert
        await Assert.That(compiled(42)).IsEqualTo(42);
        await Assert.That(compiled(100)).IsEqualTo(100);
    }

    [Test]
    public async Task Parameter_StringType_CompilesAndExecutesWithValue()
    {
        // Arrange
        var param = new Parameter("name", TypeReference.To<string>());

        // Act
        var compiled = param.CompileLambda<Func<string, string>>((param, typeof(string)));

        // Assert
        await Assert.That(compiled("hello")).IsEqualTo("hello");
        await Assert.That(compiled("world")).IsEqualTo("world");
    }

    [Test]
    public async Task Parameter_DoubleType_CompilesAndExecutesWithValue()
    {
        // Arrange
        var param = new Parameter("value", TypeReference.To<double>());

        // Act
        var compiled = param.CompileLambda<Func<double, double>>((param, typeof(double)));

        // Assert
        await Assert.That(compiled(3.14)).IsEqualTo(3.14);
        await Assert.That(compiled(2.71)).IsEqualTo(2.71);
    }

    [Test]
    public async Task Parameter_BoolType_CompilesAndExecutesWithValue()
    {
        // Arrange
        var param = new Parameter("flag", TypeReference.To<bool>());

        // Act
        var compiled = param.CompileLambda<Func<bool, bool>>((param, typeof(bool)));

        // Assert
        await Assert.That(compiled(true)).IsTrue();
        await Assert.That(compiled(false)).IsFalse();
    }

    [Test]
    public async Task Parameter_MultipleParameters_CompilesAndExecutes()
    {
        // Arrange
        var x = new Parameter("x", TypeReference.To<int>());
        var y = new Parameter("y", TypeReference.To<int>());

        // Act - Just return the first parameter
        var compiled = x.CompileLambda<Func<int, int, int>>((x, typeof(int)), (y, typeof(int)));

        // Assert
        await Assert.That(compiled(10, 20)).IsEqualTo(10);
        await Assert.That(compiled(5, 15)).IsEqualTo(5);
    }

    [Test]
    public async Task Parameter_WithoutTypeHint_CompilesAsObject()
    {
        // Arrange
        var param = new Parameter("value");

        // Act
        var compiled = param.CompileLambda<Func<object, object>>((param, typeof(object)));

        // Assert - Can accept any object
        await Assert.That(compiled(42)).IsEqualTo(42);
        await Assert.That(compiled("test")).IsEqualTo("test");
    }

    [Test]
    public async Task Parameter_SameParameterTwice_ReturnsSameExpression()
    {
        // Arrange
        var param = new Parameter("x", TypeReference.To<int>());
        var node = new Add(param, param);

        // Act
        var (expr, parameters) = node.BuildExpressionWithParameters((param, typeof(int)));
        var binary = (System.Linq.Expressions.BinaryExpression)expr;

        // Assert - Both uses of the parameter should share the same expression instance
        await Assert.That(ReferenceEquals(binary.Left, binary.Right)).IsTrue();
        await Assert.That(ReferenceEquals(binary.Left, parameters[0])).IsTrue();
    }
}
