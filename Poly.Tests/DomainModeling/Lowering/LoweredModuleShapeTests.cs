using Poly.Ast.Nodes;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// L-04a (#683, #825): the CS0127 class made structurally impossible. A void body cannot hold a failure channel, so the
/// lowered module has none: a constructor is assignments only (no <c>Return</c> of a value, no <c>throw</c>, no call of
/// a result-returning method), no void method returns a value, and the initial entry lives in the result-returning
/// <c>__EnterInitialStage</c> that <c>Create</c> calls and whose failure it propagates. This walks the lowered module
/// of every lowerable golden domain plus the entry-form fixtures, so the next entry form that lowers into a void body
/// goes red here, before the compiler.
/// </summary>
public class LoweredModuleShapeTests {
    private static readonly string[] EntryMachinery = [
        "InitializeSubscriptions", "__EnterInitialStage", "NoteAutomaticStage",
    ];

    private static bool IsVoid(MethodDefinitionNode method) => method.ReturnType is TypeReference { TypeName: "void" };

    private static bool IsResultReturningCall(Node node) => node is Invoke { Delegate: Member { MemberName: var name } }
        && (EntryMachinery.Contains(name, StringComparer.Ordinal)
            || name.StartsWith("Create", StringComparison.Ordinal)
            || name.StartsWith("Attach", StringComparison.Ordinal)
            || name.StartsWith("Register", StringComparison.Ordinal)
            || name.StartsWith("Notify", StringComparison.Ordinal));

    private static IEnumerable<(string Source, List<TypeDefinitionNode> Types)> Corpus() {
        foreach (var domain in GoldenDomains.Lowerable.OrderBy(d => d.Path, StringComparer.Ordinal))
            yield return (domain.Path, ActionScopeTouchShapeTests.Lower(File.ReadAllText(Path.Combine(GoldenDomains.RepoRoot, domain.Path))).ToList());
        foreach (var (name, dsl) in InitialEntryFailureTests.EntryForms.OrderBy(f => f.Key, StringComparer.Ordinal))
            yield return ($"entry form {name}", ActionScopeTouchShapeTests.Lower(dsl).ToList());
    }

    /// <summary>Every shape problem in <paramref name="types"/>, as <c>Type.Member: what</c>.</summary>
    internal static List<string> Problems(IEnumerable<TypeDefinitionNode> types) {
        var problems = new List<string>();
        foreach (var type in types) {
            foreach (var constructor in type.Constructors ?? []) {
                if (constructor.Body is null)
                    continue;
                foreach (var node in ActionScopeTouchShapeTests.PreOrder(constructor.Body)) {
                    if (node is Return { Value: not null })
                        problems.Add($"{type.Name}.ctor: returns a value");
                    else if (node is ThrowStatement)
                        problems.Add($"{type.Name}.ctor: throws");
                    else if (IsResultReturningCall(node))
                        problems.Add($"{type.Name}.ctor: calls {((Member)((Invoke)node).Delegate).MemberName}");
                }
            }
            foreach (var method in type.Methods ?? []) {
                if (method.Body is null || !IsVoid(method))
                    continue;
                if (ActionScopeTouchShapeTests.PreOrder(method.Body).Any(n => n is Return { Value: not null }))
                    problems.Add($"{type.Name}.{method.Name}: a void method returns a value");
            }
        }
        return problems;
    }

    [Test]
    public async Task LoweredModule_HasNoFailureChannelInAVoidBody() {
        var problems = new List<string>();
        foreach (var (source, types) in Corpus())
            problems.AddRange(Problems(types).Select(p => $"{source}: {p}"));

        await Assert.That(string.Join("\n", problems)).IsEqualTo("");
    }

    // The absence assertion above must not pass vacuously: the corpus holds the new method, a Create that calls it and
    // propagates its failure, and a constructor that still assigns.
    [Test]
    public async Task LoweredModule_CorpusHoldsTheEntryMethodItsCallAndThePropagation() {
        var types = Corpus().SelectMany(c => c.Types).ToList();
        var entryMethods = types.SelectMany(t => t.Methods ?? []).Where(m => m.Name == "__EnterInitialStage").ToList();
        var creates = types.SelectMany(t => t.Methods ?? []).Where(m => m.Name == "Create" && m.IsStatic && m.Body is not null).ToList();
        var callsEntry = creates.Count(m => ActionScopeTouchShapeTests.PreOrder(m.Body!).Any(n =>
            n is Invoke { Delegate: Member { MemberName: "__EnterInitialStage" } }));
        var propagates = creates.Count(m => ActionScopeTouchShapeTests.PreOrder(m.Body!).Any(n =>
            n is NullForgiving { Operand: Member { MemberName: "Error" } }));
        var assigningCtors = types.SelectMany(t => t.Constructors ?? []).Count(c => c.Body is not null
            && ActionScopeTouchShapeTests.PreOrder(c.Body).Any(n => n is Assignment));

        await Assert.That(entryMethods.Count).IsGreaterThan(0);
        await Assert.That(entryMethods.All(m => m.ReturnType is NamedTypeReference { TypeName: "DomainResult" })).IsTrue();
        await Assert.That(callsEntry).IsEqualTo(entryMethods.Count);
        await Assert.That(propagates).IsGreaterThan(0);
        await Assert.That(assigningCtors).IsGreaterThan(0);
    }

    // The tripwire is not vacuous either: a constructor that returns a value, throws or calls a result-returning method
    // is named.
    [Test]
    public async Task Problems_WhenAConstructorHoldsAFailureChannel_NamesIt() {
        var bad = new TypeDefinitionNode("Bad", Constructors: [new ConstructorDefinitionNode(
            Body: new Block([
                new Return(new Constant(1)),
                new ThrowStatement(new New(new NamedTypeReference("Exception"))),
                new Invoke(new Member(new ThisReference(), "__EnterInitialStage")),
            ]))]);

        var problems = Problems([bad]);

        await Assert.That(problems).IsEquivalentTo(["Bad.ctor: returns a value", "Bad.ctor: throws", "Bad.ctor: calls __EnterInitialStage"]);
    }
}
