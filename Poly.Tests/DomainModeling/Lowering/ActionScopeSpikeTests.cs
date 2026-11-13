using Poly.Interpretation;
using Poly.Interpretation.CSharp;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Runtime;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// L-03a S0 (#683, A6): a hand-built tree, no domain and no lowering, that pins the platform
/// facts the action-scope wrapper rests on: the <see cref="ActionScopeToken"/> struct local
/// through a frame slot, <c>Begin(null)</c>, <c>Touch</c> on a bag then <c>Rollback</c>, a
/// <c>TryFinally</c> whose try block ends in <c>Return</c> on the VM, and the same tree printed
/// and compiled by Roslyn with warnings as errors.
/// </summary>
public class ActionScopeSpikeTests {
    private static readonly TypeReference Scope = new ClrTypeReference(typeof(ActionScope));
    private static readonly TypeReference BagType = new ClrTypeReference(typeof(Dictionary<string, object?>));

    private static Node ScopeCall(string name, params Node[] arguments) =>
        new Invoke(new Member(Scope, name), arguments);

    /// <summary><c>try { Touch(bag); bag.Clear(); if (commit) { Commit; done = true; } return 1; }
    /// finally { if (!done) Rollback }</c></summary>
    private static (Node Body, Parameter Bag, Parameter Commit) Tree() {
        var bag = new Parameter("bag", BagType);
        var commit = new Parameter("commit", TypeReference.To<bool>());
        var scope = new Variable("__scope");
        var done = new Variable("__done");
        var body = new Block([
            new Assignment(scope, ScopeCall("Begin", new Constant(null))),
            new Assignment(done, new Constant(false)),
            new TryCatchFinally(
                new Block([
                    ScopeCall("Touch", bag),
                    new Invoke(new Member(bag, "Clear")),
                    new IfStatement(commit, new Block([
                        ScopeCall("Commit", scope),
                        new Assignment(done, new Constant(true))
                    ])),
                    new Return(new Constant(1L))
                ]),
                FinallyBlock: new Block([
                    new IfStatement(new Not(done), ScopeCall("Rollback", scope))
                ]))
        ], [scope, done]);
        return (body, bag, commit);
    }

    [Test]
    public async Task Vm_BeginTouchTryFinallyReturn_RollsBackOrCommits() {
        var (body, _, _) = Tree();
        var program = Interpreter.Compile(body);

        var rolledBack = new Dictionary<string, object?> { ["x"] = 1L };
        try {
            using var exec = Interpreter.Execute(program, s => s.SetArgs(rolledBack, false));
            await Assert.That(exec.Result.GetValue<long>()).IsEqualTo(1L);
        }
        finally {
            await Assert.That(ActionScope.HasOpenScope).IsFalse();
        }
        await Assert.That(rolledBack["x"]).IsEqualTo(1L);

        var committed = new Dictionary<string, object?> { ["x"] = 1L };
        using var exec2 = Interpreter.Execute(program, s => s.SetArgs(committed, true));
        await Assert.That(exec2.Result.GetValue<long>()).IsEqualTo(1L);
        await Assert.That(committed.Count).IsEqualTo(0);
        await Assert.That(ActionScope.HasOpenScope).IsFalse();
    }

    /// <summary>
    /// L-03c S0: the shapes L-03c emits, on the VM and a bag. <c>ActionScope.Create(bag, "Item")</c> records the bag;
    /// a throw through the <c>TryFinally</c> rolls the touched bag back; and the notify cascade's absolute exit (an
    /// int local captured before the <c>try</c> and written back in the <c>finally</c>, after the rollback) survives
    /// the unwind. <c>__depth = CreatedInstances.Count; Touch(bag); Create(bag, "Item"); __created =
    /// CreatedInstances.Count; try { bag.Clear(); throw } finally { Rollback; bag["d"] = __depth;
    /// bag["created"] = __created }</c>, the writes spelled <c>Remove</c>/<c>Add</c> because the VM indexes a
    /// hand-built <c>IndexAccess</c> as an array (the module writes bag fields through <c>this</c> members instead).
    /// </summary>
    [Test]
    public async Task Vm_CreateRecordsTheBag_AThrowRollsBack_AndTheExitAssignsTheEntryDepth() {
        var bag = new Parameter("bag", BagType);
        var scope = new Variable("__scope");
        var depth = new Variable("__depth");
        var created = new Variable("__created");
        // Cast to its own type, as the wrapper casts its token: the VM analysis does not resolve a static member's type.
        Node CreatedCount() => new TypeCast(new Member(new Member(Scope, "CreatedInstances"), "Count"), TypeReference.To<int>());
        Node Put(string key, Node value) => new Block([
            new Invoke(new Member(bag, "Remove"), new Constant(key)),
            new Invoke(new Member(bag, "Add"), new Constant(key), value)
        ]);
        var body = new Block([
            new Assignment(scope, ScopeCall("Begin", new Constant(null))),
            new Assignment(depth, CreatedCount()),
            ScopeCall("Touch", bag),
            ScopeCall("Create", bag, new Constant("Item")),
            new Assignment(created, CreatedCount()),
            new TryCatchFinally(
                new Block([
                    new Invoke(new Member(bag, "Clear")),
                    new ThrowStatement(new New(new ClrTypeReference(typeof(InvalidOperationException)), new Constant("boom")))
                ]),
                FinallyBlock: new Block([
                    ScopeCall("Rollback", scope),
                    Put("d", depth),
                    Put("created", created)
                ]))
        ], [scope, depth, created]);
        var program = Interpreter.Compile(body);

        var state = new Dictionary<string, object?> { ["d"] = 7, ["x"] = 1L };
        Exception? thrown = null;
        try {
            using var exec = Interpreter.Execute(program, s => s.SetArgs(state));
        }
        catch (Exception ex) {
            thrown = ex;
        }

        await Assert.That(thrown?.Message == "boom").IsTrue().Because(thrown?.ToString() ?? "no throw");
        await Assert.That(state["created"]).IsEqualTo(1);
        await Assert.That(state["x"]).IsEqualTo(1L);
        await Assert.That(state["d"]).IsEqualTo(0);
        await Assert.That(ActionScope.HasOpenScope).IsFalse();
    }

    [Test]
    public async Task Print_SameTree_RoslynCompilesWithWarningsAsErrors() {
        var (body, bag, commit) = Tree();
        var type = new TypeDefinitionNode(
            "Spike",
            Methods: [
                new MethodDefinitionNode(
                    "Run",
                    TypeReference.To<long>(),
                    [bag, commit],
                    body,
                    IsStatic: true)
            ]);
        var source = new CSharpWriter().Write([type]);

        await Assert.That(source).Contains("Poly.Runtime.ActionScope.Begin(null)");
        await Assert.That(source).Contains("finally");

        var diagnostics = ExportedCSharp.CompileWithWarningsAsErrors("ActionScopeSpike", [("Spike.cs", source)]);
        await Assert.That(string.Join("\n", diagnostics.Select(d => d.ToString()))).IsEqualTo("");

        var assembly = ExportedCSharp.CompileAndLoad(source, "ActionScopeSpikeRun");
        var run = assembly.GetType("Spike")!.GetMethod("Run")!;

        var rolledBack = new Dictionary<string, object?> { ["x"] = 1L };
        await Assert.That(run.Invoke(null, [rolledBack, false])).IsEqualTo(1L);
        await Assert.That(rolledBack["x"]).IsEqualTo(1L);

        var committed = new Dictionary<string, object?> { ["x"] = 1L };
        await Assert.That(run.Invoke(null, [committed, true])).IsEqualTo(1L);
        await Assert.That(committed.Count).IsEqualTo(0);
        await Assert.That(ActionScope.HasOpenScope).IsFalse();
    }
}