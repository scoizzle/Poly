using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;

namespace Poly.Tests.Interpretation;

/// <summary>F3: SuspendNode + ExecutionResult.Resume / Resuming PC dispatch.</summary>
public class SuspendResumeVmTests {
    [Test]
    public async Task Resume_WhenNotSuspended_Throws() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new Constant(1L)));
        await Assert.That(exec.IsSuspended).IsFalse();
        await Assert.That(() => { using var _ = exec.Resume(); })
            .Throws<InvalidOperationException>()
            .WithMessageContaining("not suspended");
    }

    [Test]
    public async Task Suspend_ThenResume_FallsThroughToLaterStatements() {
        var x = new Variable("x");
        var node = new Block([
            new Assignment(x, new Constant(1L)),
            new SuspendNode(new Constant(0L), "mid"),
            new Assignment(x, new Constant(2L)),
            x
        ], [x]);
        var program = Interpreter.Compile(node);
        using var exec = Interpreter.Execute(program);
        await Assert.That(exec.IsSuspended).IsTrue();
        await Assert.That(exec.State.Status).IsEqualTo(InterpreterStatus.Suspended);
        using var resumed = exec.Resume();
        await Assert.That(resumed.IsSuspended).IsFalse();
        await Assert.That(resumed.Result.GetValue<long>()).IsEqualTo(2L);
    }

    [Test]
    public async Task SuspendInFunctionBody_IsACompileRejection() {
        var typeDef = new TypeDefinitionNode(
            "Widget",
            "Sample",
            Methods: [
                new MethodDefinitionNode(
                    "Tick",
                    new PrimitiveTypeReference(Poly.Introspection.PrimitiveType.Int64),
                    Body: new Block([
                        new SuspendNode(new Constant(0L), "mid"),
                        new Constant(7L)
                    ]))
            ]);
        var analyzer = new TypeDefinitionNodeAnalyzer();
        var ctx = AnalysisContext.CreateDefault();
        analyzer.Analyze(ctx, typeDef);
        var invoke = new Invoke(
            new Member(new Parameter("entity", new TypeReference("Sample.Widget")), "Tick"));

        await Assert.That(() => Interpreter.Compile(invoke, ctx.TypeDefinitions))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("SuspendNode");
    }

    [Test]
    public async Task SuspendInLambda_IsACompileRejection() {
        var declaration = new Variable("fn");
        var x = new Parameter("x", TypeReference.To<long>());
        var node = new Block([
            new Assignment(declaration, new Lambda([x], new Block([
                new SuspendNode(new Constant(0L), "mid"),
                x
            ]))),
            new Invoke(new Variable("fn"), new Constant(6L))
        ], [declaration]);

        await Assert.That(() => Interpreter.Compile(node))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("SuspendNode");
    }

    [Test]
    public async Task Suspend_InsideSetArgsBody_ResumesAndReturnsParameter() {
        var x = new Parameter("x", TypeReference.To<long>());
        var body = new Invoke(new Lambda([x], new Block([
            new SuspendNode(new Constant(0L), "mid"),
            x
        ])));
        var program = Interpreter.Compile(body);
        using var exec = Interpreter.Execute(program, s => s.SetArgs(42L));
        await Assert.That(exec.IsSuspended).IsTrue();
        using var resumed = exec.Resume();
        await Assert.That(resumed.Result.GetValue<long>()).IsEqualTo(42L);
    }
}