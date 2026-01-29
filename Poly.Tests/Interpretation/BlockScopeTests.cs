using System.Linq.Expressions;

using Poly.Interpretation;
using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Interpretation.AbstractSyntaxTree.Arithmetic;
using Poly.Tests.TestHelpers;

using Expr = System.Linq.Expressions.Expression;

namespace Poly.Tests.Interpretation;

public class BlockScopeTests {
    [Test]
    public async Task BlockScope_CreatesNewScope_VariablesNotVisibleOutside()
    {
        // Arrange - nested blocks, inner variable should not affect outer
        var innerVar = new Variable("x");
        var innerAssign = new Assignment(innerVar, Wrap(50));
        var innerBlock = new Block([innerAssign, innerVar], new[] { innerVar });

        var outerVar = new Variable("x");
        var outerAssign = new Assignment(outerVar, Wrap(100));
        var outerBlock = new Block([outerAssign, innerBlock], new[] { outerVar });

        // Act
        var expr = outerBlock.BuildExpression();
        var compiled = Expr.Lambda<Func<int>>(expr).Compile();
        var result = compiled();

        // Assert - inner block returns 50, but outer scope still has its variable at 100
        await Assert.That(result).IsEqualTo(50);
    }

    [Test]
    public async Task BlockScope_NestedScopes_InnerShadowsOuter()
    {
        // Arrange
        var outerVar = new Variable("x");
        var outerAssign = new Assignment(outerVar, Wrap(100));

        var innerVar = new Variable("x");
        var innerAssign = new Assignment(innerVar, Wrap(50));
        var innerBlock = new Block([innerAssign, innerVar], new[] { innerVar });

        var outerBlock = new Block([outerAssign, innerBlock], new[] { outerVar });

        // Act
        var expr = outerBlock.BuildExpression();
        var compiled = Expr.Lambda<Func<int>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(50);
    }

    [Test]
    public async Task BlockScope_ExecutesExpressions_InSequence()
    {
        // Arrange
        var var1 = new Variable("a");
        var assign1 = new Assignment(var1, Wrap(10));

        var var2 = new Variable("b");
        var assign2 = new Assignment(var2, Wrap(20));

        var node = new Block([assign1, assign2, var2], new[] { var1, var2 });

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<int>>(expr).Compile();
        var result = compiled();

        // Assert - last expression should be var2 = 20
        await Assert.That(result).IsEqualTo(20);
    }

    [Test]
    public async Task BlockScope_CanAccessOuterScope_Variables()
    {
        // Arrange - outer variable used in inner block
        var outerVar = new Variable("x");
        var outerAssign = new Assignment(outerVar, Wrap(100));

        var addExpr = outerVar.Add(Wrap(50));
        var outerBlock = new Block([outerAssign, addExpr], new[] { outerVar });

        // Act
        var expr = outerBlock.BuildExpression();
        var compiled = Expr.Lambda<Func<int>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(150);
    }

    [Test]
    public async Task BlockScope_MultipleBlocks_IndependentScopes()
    {
        // Arrange
        var block1Var = new Variable("x");
        var block1Assign = new Assignment(block1Var, Wrap(10));
        var block1 = new Block([block1Assign, block1Var], new[] { block1Var });

        var block2Var = new Variable("x");
        var block2Assign = new Assignment(block2Var, Wrap(20));
        var block2 = new Block([block2Assign, block2Var], new[] { block2Var });

        // Wrap both blocks together
        var combined = new Block(block1, block2);

        // Act
        var expr = combined.BuildExpression();
        var compiled = Expr.Lambda<Func<int>>(expr).Compile();
        var result = compiled();

        // Assert - should return last block's value
        await Assert.That(result).IsEqualTo(20);
    }
}