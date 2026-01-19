using Poly.Tests.TestHelpers;
using System.Linq.Expressions;

using Poly.Interpretation;
using Expr = System.Linq.Expressions.Expression;
using Poly.Interpretation.AbstractSyntaxTree;

namespace Poly.Tests.Interpretation;

public class BlockScopeTests {
    [Test]
    public async Task Block_CreatesNewScope_VariablesNotVisibleOutside()
    {
        var context = new InterpretationContext();

        // Verify 'y' doesn't exist initially
        var yBefore = context.GetVariable("y");
        await Assert.That(yBefore).IsNull();

        // Push scope and declare 'y' within a block's scope
        context.PushScope();
        var y = context.DeclareVariable("y", Wrap(10));
        await Assert.That(context.GetVariable("y")).IsNotNull();
        context.PopScope();

        // After popping, 'y' should not be accessible
        var yAfter = context.GetVariable("y");
        await Assert.That(yAfter).IsNull();
    }

    [Test]
    public async Task Block_NestedScopes_InnerShadowsOuter()
    {
        var context = new InterpretationContext();

        // Declare 'x' in outer scope
        var outerX = context.DeclareVariable("x", Wrap(5));

        // Inner block declares its own 'x'
        context.PushScope();
        var innerX = context.DeclareVariable("x", Wrap(10));

        // Inner 'x' should be different from outer 'x'
        await Assert.That(innerX).IsNotEqualTo(outerX);

        // Current scope should see inner 'x'
        var currentX = context.GetVariable("x");
        await Assert.That(currentX).IsEqualTo(innerX);

        context.PopScope();

        // After popping, should see outer 'x' again
        currentX = context.GetVariable("x");
        await Assert.That(currentX).IsEqualTo(outerX);
    }

    [Test]
    public async Task Block_ExecutesExpressionsInSequence()
    {
        var context = new InterpretationContext();
        var x = context.AddParameter<int>("x");
        var y = context.AddParameter<int>("y");

        // Create a block that adds two values
        var block = new Block(
            x.Add(Wrap(5)),
            y.Multiply(Wrap(2)),
            x.Add(y)  // Last expression determines return value
        );

        // Build expression - Block pushes/pops its own scope
        var expr = block.BuildExpression(context);
        var lambda = Expr.Lambda<Func<int, int, int>>(
            expr,
            x.GetParameterExpression(context),
            y.GetParameterExpression(context)
        );
        var compiled = lambda.Compile();

        // The block returns the last expression: x + y
        await Assert.That(compiled(10, 5)).IsEqualTo(15);
    }

    [Test]
    public async Task Block_CanAccessOuterScopeVariables()
    {
        var context = new InterpretationContext();

        // Declare variable in outer scope
        var outerVar = context.DeclareVariable("outer", Wrap(100));

        // Inner block should be able to access 'outer'
        context.PushScope();
        var innerAccess = context.GetVariable("outer");
        await Assert.That(innerAccess).IsEqualTo(outerVar);
        context.PopScope();
    }

    [Test]
    public async Task Block_MultipleBlocks_IndependentScopes()
    {
        var context = new InterpretationContext();

        // First block declares 'a'
        context.PushScope();
        var a1 = context.DeclareVariable("a", Wrap(1));
        context.PopScope();

        // 'a' should not be visible
        var aAfterFirst = context.GetVariable("a");
        await Assert.That(aAfterFirst).IsNull();

        // Second block declares 'a' independently
        context.PushScope();
        var a2 = context.DeclareVariable("a", Wrap(2));
        context.PopScope();

        // These should be different variables
        await Assert.That(a1).IsNotEqualTo(a2);
    }
}