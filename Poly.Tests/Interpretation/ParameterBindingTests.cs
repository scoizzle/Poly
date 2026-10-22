using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.CSharp;
using Poly.Interpretation.Vm;
using Poly.Introspection;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Reader-shaped method bodies use <see cref="Variable"/> nodes for parameters, not
/// <see cref="Parameter"/> nodes. The bind pass must resolve those uses to the method
/// parameters before the type pass projects their declared types.
/// </summary>
public class ParameterBindingTests {
    private static Member Probe(string name) =>
        new(new ClrTypeReference(typeof(ParameterBindingTests)), name);

    public static int CountIf(Func<int, bool> predicate, int value) =>
        predicate(value) ? 1 : 0;

    public static Func<int, bool> HostPredicate() => value => value > 0;

    private static ITypeDefinitionProvider Analyze(TypeDefinitionNode type) {
        var analyzer = new TypeDefinitionNodeAnalyzer();
        var ctx = AnalysisContext.CreateDefault();
        analyzer.Analyze(ctx, type);
        return ctx.TypeDefinitions;
    }

    [Test]
    public async Task MethodParameter_ReaderShapedUse_BindsAndInvokes() {
        var predicate = new Parameter("pred", TypeReference.To<Func<int, bool>>());
        var type = new TypeDefinitionNode(
            "Forwarder", "Sample",
            Methods: [
                new MethodDefinitionNode(
                    "Count",
                    TypeReference.To<long>(),
                    Parameters: [predicate],
                    Body: new Invoke(
                        Probe(nameof(CountIf)),
                        new Variable("pred"),
                        new Constant(5)))
            ]);
        var provider = Analyze(type);
        var forwarder = new Parameter("forwarder", new TypeReference("Sample.Forwarder"));
        var program = Interpreter.Compile(
            new Invoke(
                new Member(forwarder, "Count"),
                new Invoke(Probe(nameof(HostPredicate)))),
            provider);

        using var exec = Interpreter.Execute(program,
            state => state.SetArgs(new object?[] { new Dictionary<string, object?>(), }));

        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task MethodParameter_ReaderShapedWrite_UpdatesTheParameter() {
        var value = new Parameter("value", TypeReference.To<long>());
        var type = new TypeDefinitionNode(
            "Counter", "Sample",
            Methods: [
                new MethodDefinitionNode(
                    "Bump",
                    TypeReference.To<long>(),
                    Parameters: [value],
                    Body: new Block([
                        new Assignment(
                            new Variable("value"),
                            new Add(new Variable("value"), new Constant(1L))),
                        new Variable("value")
                    ]))
            ]);
        var provider = Analyze(type);
        var counter = new Parameter("counter", new TypeReference("Sample.Counter"));
        var program = Interpreter.Compile(
            new Invoke(new Member(counter, "Bump"), new Constant(4L)),
            provider);

        using var exec = Interpreter.Execute(program,
            state => state.SetArgs(new object?[] { new Dictionary<string, object?>(), }));

        await Assert.That(exec.GetValue<long>()).IsEqualTo(5L);
    }

    [Test]
    public async Task ZeroArgLambda_FirstParameterWrite_IsVisible() {
        // A zero-argument invocation binds the lambda's first parameter through
        // SetArgs at slot 0. A write must update the same slot later reads use.
        var result = CSharpScript.Run("((long x) => { x = x + 1; return x; })()");

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value).IsEqualTo("1");
    }

    [Test]
    public async Task MethodParameter_MemberAccess_Resolves() {
        var text = new Parameter("text", TypeReference.To<string>());
        var type = new TypeDefinitionNode(
            "Formatter", "Sample",
            Methods: [
                new MethodDefinitionNode(
                    "HasLength",
                    TypeReference.To<bool>(),
                    Parameters: [text],
                    Body: new GreaterThan(
                        new Member(new Variable("text"), nameof(string.Length)),
                        new Constant(0)))
            ]);
        var provider = Analyze(type);
        var formatter = new Parameter("formatter", new TypeReference("Sample.Formatter"));
        var program = Interpreter.Compile(
            new Invoke(new Member(formatter, "HasLength"), new Constant("abc")),
            provider);

        using var exec = Interpreter.Execute(program,
            state => state.SetArgs(new object?[] { new Dictionary<string, object?>(), }));

        await Assert.That(exec.GetValue<bool>()).IsTrue();
    }

    [Test]
    public async Task MethodParameter_AssigningStringToInt_FailsClosed() {
        var value = new Parameter("value", TypeReference.To<int>());
        var type = new TypeDefinitionNode(
            "Writer", "Sample",
            Methods: [
                new MethodDefinitionNode(
                    "Write",
                    TypeReference.To<long>(),
                    Parameters: [value],
                    Body: new Assignment(new Variable("value"), new Constant("hi")))
            ]);

        var analysis = Interpreter.Analyze(type);

        await Assert.That(analysis.HasErrors).IsTrue();
        await Assert.That(analysis.Diagnostics.Any(d => d.Message.Contains("cannot assign"))).IsTrue();
        await Assert.That(analysis.Diagnostics.Any(d => d.Message.Contains("is not declared"))).IsFalse();
    }

    [Test]
    public async Task UntypedParameterNamedSystem_DottedName_IsNotStolen() {
        // A bound root wins over a dotted CLR type even when its type is unknown;
        // otherwise the unguarded type-pass fallback rewrites the member.
        var system = new Parameter("System");
        var member = new Member(new Variable("System"), "Console");
        var lambda = new Lambda([system], member);

        var analysis = Interpreter.Analyze(lambda);

        await Assert.That(analysis.GetNodeReplacement(member)).IsNull();
    }

    [Test]
    public async Task ShadowedName_OuterUseAfterInnerScope_KeepsOuterType() {
        // Both declarations are still live in the map (NodeId distinguishes them);
        // the inner block's use takes int and the trailing use takes double.
        var result = CSharpScript.Run("double x = 1.5; { int x = 2; int q = x; } x");

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value).IsEqualTo("1.5");
    }

    [Test]
    public async Task LocalNamedSystem_IsNotRewrittenToConsole() {
        var local = CSharpScript.Run("int System = 1; System + 1");
        await Assert.That(local.Success).IsTrue();
        await Assert.That(local.Value).IsEqualTo("2");

        // The dotted rewrite must not steal a declared local named `System` and turn
        // this into the CLR type's member.
        var collided = CSharpScript.Run("int System = 1; System.Console");
        await Assert.That(collided.Success).IsFalse();
        await Assert.That(collided.Diagnostics.Any(d => d.Contains("does not contain a member named 'Console'"))).IsTrue();
    }

    [Test]
    public async Task DottedType_AfterSameRootLeavesScope_StillResolves() {
        // The scope map keeps a popped name as an empty stack; it must not shadow
        // the type name after its block ends.
        var result = CSharpScript.Run("{ int System = 1; } System.String.Empty");

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Diagnostics.Any(d => d.Contains("is not declared"))).IsFalse();
    }

    [Test]
    public async Task ShadowedLoopVariable_DoesNotTakeTheOuterType() {
        // The reader accepts a for-loop variable that shadows a local of another
        // type. The body's use must project the loop declaration (double), not the
        // outer int; the raw int read of a double slot would be a huge value.
        var result = CSharpScript.Run(
            "int x = 5; double r = 0; var flag = true; " +
            "for (double x = 1; flag; x = x + 1) { r = x; flag = false; } r");

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value).IsEqualTo("1");
    }
}