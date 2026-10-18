using Poly.Ast.Nodes;
using Poly.Interpretation;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.Interpretation;

/// <summary>
/// A source (reader-built) lambda parameter used in the lambda body. The reader
/// emits a fresh <see cref="Variable"/> per identifier, so the body does not
/// reference the lambda's <see cref="Parameter"/> node; these tests pin the
/// by-name parameter resolution that inline invocation needs.
/// </summary>
public class LambdaParameterVmTests {
    private static Node ReadFirstStatement(string source) =>
        CSharpLanguage.Read(source + ";").TopLevelStatements![0];

    private static long Execute(Node node) {
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        return exec.RawValue;
    }

    [Test]
    public async Task InlineLambda_ParameterRead_ReturnsArgument() {
        // ((long a) => a + 1)(4) → 5
        var node = ReadFirstStatement("((long a) => a + 1)(4)");
        await Assert.That(Execute(node)).IsEqualTo(5L);
    }

    [Test]
    public async Task InlineLambda_ParameterShadowsOuterVariable_UsesArgument() {
        // { var s = "hi"; ((long s) => s + 1)(4) } → 5, not the outer "hi" handle.
        var invoke = ReadFirstStatement("((long s) => s + 1)(4)");
        var outer = new Variable("s");
        var node = new Block([
            new Assignment(outer, new Constant("hi")),
            invoke
        ], [outer]);
        await Assert.That(Execute(node)).IsEqualTo(5L);
    }

    [Test]
    public async Task InlineLambda_TypedParameter_MemberAccessResolves() {
        // ((string s) => s.Length)("hi") → 2
        var node = ReadFirstStatement("((string s) => s.Length)(\"hi\")");
        await Assert.That(Execute(node)).IsEqualTo(2L);
    }

    [Test]
    public async Task InlineLambda_ParameterWrite_UpdatesLocalParameter() {
        // ((long a) => { a = a + 1; return a; })(4) → 5
        var node = ReadFirstStatement("((long a) => { a = a + 1; return a; })(4)");
        await Assert.That(Execute(node)).IsEqualTo(5L);
    }

    [Test]
    public async Task InlineLambda_ArgumentsAreCallerScoped_NotParameters() {
        // { var x = 100; var y = 20; ((long x, long y) => x + y)(y, x) } → 120.
        // The second argument `x` must read the outer x, not the lambda parameter.
        var invoke = ReadFirstStatement("((long x, long y) => x + y)(y, x)");
        var outerX = new Variable("x");
        var outerY = new Variable("y");
        var node = new Block([
            new Assignment(outerX, new Constant(100L)),
            new Assignment(outerY, new Constant(20L)),
            invoke
        ], [outerX, outerY]);
        await Assert.That(Execute(node)).IsEqualTo(120L);
    }

    [Test]
    public async Task InlineLambda_NestedSameParameterName_InnerShadowsOuter() {
        // ((long a) => ((long a) => a + 1)(a))(4) → 5
        var node = ReadFirstStatement("((long a) => ((long a) => a + 1)(a))(4)");
        await Assert.That(Execute(node)).IsEqualTo(5L);
    }

    [Test]
    public async Task InlineLambda_CapturesOuterVariableAlongsideParameter() {
        // { var n = 10; ((long a) => a + n)(4) } → 14
        var invoke = ReadFirstStatement("((long a) => a + n)(4)");
        var n = new Variable("n");
        var node = new Block([
            new Assignment(n, new Constant(10L)),
            invoke
        ], [n]);
        await Assert.That(Execute(node)).IsEqualTo(14L);
    }

    [Test]
    public async Task InlineLambda_UntypedParameterMemberAccess_FailsClosed() {
        // (s => s.Length)("hi"): `s` has no declared type, so `Length` cannot be
        // resolved. It must fail loudly rather than guess a binding.
        var node = ReadFirstStatement("(s => s.Length)(\"hi\")");
        await Assert.That(() => Interpreter.Execute(Interpreter.Compile(node)))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("not resolved");
    }
}