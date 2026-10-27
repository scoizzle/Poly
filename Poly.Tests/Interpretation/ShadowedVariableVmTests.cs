using Poly.Interpretation;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Shadowing must bind to the innermost declaration in the VM emitter, not
/// to whichever same-named variable happens to be found first. Each use is a
/// distinct <see cref="Variable"/> node (the reader makes one per identifier).
/// The bind pass records the declaration; the emitter follows that edge.
/// </summary>
public class ShadowedVariableVmTests {
    private static Node ReadBody(string body) =>
        CSharpLanguage.Read($"class C {{ void M() {{ {body} }} }}")
            .Types[0].Members.OfType<MethodDefinitionNode>().Single().Body!;

    private static long Run(string body) {
        using var exec = Interpreter.Execute(Interpreter.Compile(ReadBody(body)));
        return exec.GetValue<long>();
    }

    [Test]
    public async Task InnerShadowing_UsesInnerDeclaration() {
        // The outer s is a string; the inner s is an int. n = s + 1 must read
        // the inner s. The bug read the outer s handle and returned 2.
        await Assert.That(Run("""var s = "hi"; var n = 0; { var s = 5; n = s + 1; } n;"""))
            .IsEqualTo(6L);
    }

    [Test]
    public async Task TwoLevelShadowing_EachLevelBindsItsOwnDeclaration() {
        // 0 + 100 (innermost) = 100; + 10 (middle) = 110.
        await Assert.That(Run("""var s = 1; var n = 0; { var s = 10; { var s = 100; n = n + s; } n = n + s; } n;"""))
            .IsEqualTo(110L);
    }

    [Test]
    public async Task OuterVariable_UsedAfterInnerBlock_KeepsItsValue() {
        // n = 5 + 1 = 6 inside the block; after the block n + 100 (the outer s) = 106.
        await Assert.That(Run("""var s = 100; var n = 0; { var s = 5; n = s + 1; } n = n + s; n;"""))
            .IsEqualTo(106L);
    }

    [Test]
    public async Task AssigningHeapValueToScalarLocal_FailsClosed() {
        // n's slot was established as a long by `var n = 0`; storing the string
        // heap handle there must be rejected, not silently reinterpreted.
        var body = ReadBody("""var n = 0; n = "hi"; n;""");
        await Assert.That(() => Interpreter.Compile(body))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("cannot assign 'String' to variable 'n'");
    }

    [Test]
    public async Task LambdaParameter_ShadowsOuterLocal() {
        // The body use is a fresh Variable. The parameter wins: 4 + 1, not the outer string.
        await Assert.That(Run("""var s = "hi"; var n = ((long s) => s + 1)(4); n;"""))
            .IsEqualTo(5L)
            .Because("the lambda parameter shadows the outer local; binding the use to that local would not yield 5");
    }

    [Test]
    public async Task NestedLambdaParameters_SameName_BindToTheirOwnParameter() {
        // Inner parameter is 5, the enclosing parameter is 10, and the outer local 1000 stays hidden.
        await Assert.That(Run("""var s = 1000; var n = ((long s) => ((long s) => s + 1)(5) + s)(10); n;"""))
            .IsEqualTo(16L)
            .Because("each lambda parameter is its own binding: 5 + 1 + 10");
    }

    [Test]
    public async Task CatchVariable_ShadowsOuterLocal_AndDoesNotClobberIt() {
        // Inside the catch, s is the exception. After it, s is still the outer 5.
        await Assert.That(Run("""
            var s = 5L;
            var n = 0L;
            try {
                throw new System.InvalidOperationException("hi");
            } catch (System.InvalidOperationException s) {
                n = s.Message.Length;
            }
            n + s;
            """))
            .IsEqualTo(7L)
            .Because("the catch name reads the exception (\"hi\".Length is 2) and the outer s stays 5");
    }

    [Test]
    public async Task QuotedLambdaParameter_ShadowsCapturedOuterOfTheSameName() {
        // A quoted body use is a fresh Variable, distinct from both the parameter and the outer local.
        var outer = new Variable("s");
        var param = new Parameter("s", TypeReference.To<long>());
        var lambda = new Lambda([param], new Add(new Variable("s"), new Constant(1L)));
        var node = new Block([
            new Assignment(outer, new Constant(100L)),
            new Invoke(Probe(nameof(ShadowProbe.Apply)), lambda, new Constant(5L))
        ], [outer]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(6L)
            .Because("the quoted parameter shadows the outer local; binding the fresh use to the capture would return 101");
    }

    [Test]
    public async Task QuotedLambda_TwoUsesOfCapturedLocal_SeeTheSameValue() {
        // Two fresh uses of one captured local. Both must read 3: 10 + 3 + 3.
        var outer = new Variable("s");
        var x = new Parameter("x", TypeReference.To<long>());
        var lambda = new Lambda([x], new Add(new Add(x, new Variable("s")), new Variable("s")));
        var node = new Block([
            new Assignment(outer, new Constant(3L)),
            new Invoke(Probe(nameof(ShadowProbe.Apply)), lambda, new Constant(10L))
        ], [outer]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(16L)
            .Because("both fresh uses of the captured local read 3");
    }

    [Test]
    public async Task InlineParameterUse_FreshVariable_ReadsTheParameter() {
        var n = new Parameter("n", TypeReference.To<long>());
        var lambda = new Lambda([n], new Add(new Variable("n"), new Constant(1L)));
        using var exec = Interpreter.Execute(Interpreter.Compile(new Invoke(lambda, new Constant(9L))));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(10L)
            .Because("a fresh Variable named n is that lambda parameter");
    }

    private static Member Probe(string name) =>
        new(new ClrTypeReference(typeof(ShadowProbe)), name);

    public static class ShadowProbe {
        public static long Apply(System.Linq.Expressions.Expression<Func<long, long>> body, long value) =>
            body.Compile()(value);
    }
}