using System.Linq.Expressions;

using Poly.Syntax.AbstractSyntaxTree;
using Poly.Syntax.AbstractSyntaxTree.Arithmetic;
using Poly.Syntax.AbstractSyntaxTree.Comparison;
using Poly.Tests.TestHelpers;

using Expr = System.Linq.Expressions.Expression;

namespace Poly.Tests.Interpretation;

public class LambdaInvokeTests {
    // Lambda compilation

    [Test]
    public async Task Lambda_WithNoParameters_CompilesAndReturnsConstant() {
        var lambda = new Lambda([], Wrap(42));

        var expr = lambda.BuildExpression();
        var compiled = Expr.Lambda<Func<Func<int>>>(expr).Compile();
        var result = compiled()();

        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task Lambda_WithOneParameter_ReturnsParameter() {
        var p = new Parameter("x", TypeReference.To<int>());
        var lambda = new Lambda([p], p);

        var compiled = lambda.CompileLambda<Func<Func<int, int>>>();
        var result = compiled()(7);

        await Assert.That(result).IsEqualTo(7);
    }

    [Test]
    public async Task Lambda_WithTwoParameters_ReturnsSum() {
        var x = new Parameter("x", TypeReference.To<int>());
        var y = new Parameter("y", TypeReference.To<int>());
        var lambda = new Lambda([x, y], new Add(x, y));

        var compiled = lambda.CompileLambda<Func<Func<int, int, int>>>();
        var result = compiled()(3, 4);

        await Assert.That(result).IsEqualTo(7);
    }

    // Lambda return scoping

    [Test]
    public async Task Lambda_WithReturnStatement_ExitsLambdaNotOuterScope() {
        // outer param
        var n = new Parameter("n", TypeReference.To<int>());
        // inner lambda: (x) => { if (x > 0) return 1; return -1; }
        var x = new Parameter("x", TypeReference.To<int>());
        var innerLambda = new Lambda([x],
            new Block([
                new IfStatement(new GreaterThan(x, Wrap(0)), Return.True),
                Return.False
            ], []));

        // outer: the lambda is just created and immediately invoked on n
        var invoke = new Invoke(innerLambda, n);
        var compiled = invoke.CompileLambda<Func<int, bool>>((n, typeof(int)));

        await Assert.That(compiled(5)).IsTrue();
        await Assert.That(compiled(-3)).IsFalse();
        await Assert.That(compiled(0)).IsFalse();
    }

    // Invoke

    [Test]
    public async Task Invoke_WithLambdaNode_ExecutesBody() {
        var p = new Parameter("v", TypeReference.To<int>());
        var lambda = new Lambda([p], new Add(p, Wrap(10)));
        var invoke = new Invoke(lambda, Wrap(5));

        var expr = invoke.BuildExpression();
        var result = Expr.Lambda<Func<int>>(expr).Compile()();

        await Assert.That(result).IsEqualTo(15);
    }

    [Test]
    public async Task Invoke_WithOuterParameter_PassesValueIntoLambda() {
        var outer = new Parameter("n", TypeReference.To<int>());
        var inner = new Parameter("x", TypeReference.To<int>());
        var lambda = new Lambda([inner], new Add(inner, Wrap(1)));
        var invoke = new Invoke(lambda, outer);

        var compiled = invoke.CompileLambda<Func<int, int>>((outer, typeof(int)));

        await Assert.That(compiled(9)).IsEqualTo(10);
        await Assert.That(compiled(0)).IsEqualTo(1);
    }

    [Test]
    public async Task Lambda_CapturesOuterParameter_FromLexicalParentScope() {
        var outer = new Parameter("outer", TypeReference.To<int>());
        var inner = new Parameter("inner", TypeReference.To<int>());
        var lambda = new Lambda([inner], new Add(inner, outer));
        var invoke = new Invoke(lambda, Wrap(5));

        var compiled = invoke.CompileLambda<Func<int, int>>((outer, typeof(int)));

        await Assert.That(compiled(10)).IsEqualTo(15);
        await Assert.That(compiled(0)).IsEqualTo(5);
    }

    // InvokeWith extension

    [Test]
    public async Task InvokeWith_ExtensionMethod_BuildsInvokeNode() {
        var p = new Parameter("v", TypeReference.To<int>());
        var lambda = new Lambda([p], new Add(p, Wrap(100)));
        var invoke = lambda.InvokeWith(Wrap(7));

        var expr = invoke.BuildExpression();
        var result = Expr.Lambda<Func<int>>(expr).Compile()();

        await Assert.That(result).IsEqualTo(107);
    }

    // Return statement in a plain block (no lambda) - regression for return-label injection

    [Test]
    public async Task Block_WithEarlyReturn_ReturnsEarlyValue() {
        var param = new Parameter("x", TypeReference.To<bool>());
        var block = new Block([
            new IfStatement(param, Return.True),
            Return.False
        ], []);

        var compiled = block.CompileLambda<Func<bool, bool>>((param, typeof(bool)));

        await Assert.That(compiled(true)).IsTrue();
        await Assert.That(compiled(false)).IsFalse();
    }
}