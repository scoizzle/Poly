using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.CSharp;
using Poly.Introspection;

namespace Poly.Tests.Interpretation;

/// <summary>
/// A static call on a type name is a call on a type receiver, not a chain of values (#278).
/// Resolution binds a qualified name in call-receiver position; a local of the same name always
/// wins, because a qualified-name rule that beats a local breaks ordinary programs.
/// </summary>
public class CSharpQualifiedNameTests {
    private const string QualifiedPopCountUnit =
        "class C { void M() { var n = System.Numerics.BitOperations.PopCount((ulong)7); } }";

    [Test]
    public async Task QualifiedStaticCall_ReadsAsExecutable() {
        var unit = CSharpLanguage.Read(QualifiedPopCountUnit);
        var analysis = Interpreter.Analyze(unit);
        var invoke = Find<Invoke>(unit, node => (node.Delegate as Member)?.MemberName == "PopCount");

        await Assert.That(invoke).IsNotNull();
        await Assert.That(analysis.GetResolvedMember(invoke!)).IsNotNull()
            .Because("a qualified static call resolves to the method, not to a value chain");
        await Assert.That(Errors(analysis)).IsEmpty();

        var result = CSharpScript.Run("System.Numerics.BitOperations.PopCount((ulong)7)");
        await Assert.That(result.Success).IsTrue().Because(string.Join("; ", result.Diagnostics));
        await Assert.That(result.Value).IsEqualTo("3");
    }

    [Test]
    public async Task FullyQualifiedStaticCall_Executes() {
        var result = CSharpScript.Run("System.Math.Abs(-1)");
        await Assert.That(result.Success).IsTrue().Because(string.Join("; ", result.Diagnostics));
        await Assert.That(result.Value).IsEqualTo("1");
    }

    [Test]
    public async Task UsingScope_ResolvesShortTypeName() {
        var unit = CSharpLanguage.Read("using System;\nMath.Abs(-1);");
        await Assert.That(Find<Member>(unit, node => node.MemberName == "Abs")?.Value)
            .IsTypeOf<NamedTypeReference>()
            .Because("a using names the namespace a short type name resolves through");

        await Assert.That(Execute(unit)).IsEqualTo(1L);

        var numerics = CSharpLanguage.Read("using System.Numerics;\nBitOperations.PopCount((ulong)7);");
        await Assert.That(Execute(numerics)).IsEqualTo(3L);
    }

    [Test]
    public async Task LocalNamedLikeANamespace_Wins() {
        // A declared local of the same name is a value: the chain stays a value chain, and the
        // program fails closed rather than becoming a static call.
        var unit = CSharpLanguage.Read(
            "{ long System = 5; System.Numerics.BitOperations.PopCount(1); }");
        var chain = Find<Invoke>(unit, node => (node.Delegate as Member)?.MemberName == "PopCount")?.Delegate as Member;
        await Assert.That(chain).IsNotNull();
        await Assert.That(chain!.Value).IsTypeOf<Member>()
            .Because("a local named like a namespace root keeps the chain rooted in a value");
        await Assert.That(RootOf(chain)).IsTypeOf<Variable>();

        var result = CSharpScript.Run("long System = 5; System.Numerics.BitOperations.PopCount(1);");
        await Assert.That(result.Success).IsFalse()
            .Because("a value chain has no member named Numerics, so the call cannot execute");

        // The same rule under a using: a local named like an imported type wins, so the chain is a
        // value chain read against the local's type — an error, never a static call.
        var usingUnit = CSharpLanguage.Read("using System;\nlong Math = 5;\nMath.Abs(-1);");
        await Assert.That(Find<Member>(usingUnit, node => node.MemberName == "Abs")?.Value)
            .IsTypeOf<Variable>();
        await Assert.That(() => Execute(usingUnit)).Throws<InvalidOperationException>()
            .Because("a local named like an imported type keeps the chain a value chain, which fails closed");
    }

    [Test]
    public async Task TypeReceiver_PrintsAsTheTypeName() {
        // Member(<type>, name) is the shape the VM binds for a static access. A type reference in
        // receiver position is a type, so it prints as the type name it is — a keyword-named CLR
        // type stays the keyword instead of being escaped as an identifier.
        await Assert.That(CSharpLanguage.Write(
                new Member(new NamedTypeReference("BitOperations", "System.Numerics"), "PopCount")))
            .IsEqualTo("_ = System.Numerics.BitOperations.PopCount;");
        await Assert.That(CSharpLanguage.Write(
                new Member(new PrimitiveTypeReference(PrimitiveType.Int32), "MaxValue")))
            .IsEqualTo("_ = int.MaxValue;");
    }

    [Test]
    public async Task PopCount_PrintsItsOnlyCSharpSpelling() {
        var printed = CSharpLanguage.Write(new PopCount(new Variable("x")));
        await Assert.That(printed).IsEqualTo("_ = System.Numerics.BitOperations.PopCount((ulong)x);");
    }

    [Test]
    public async Task PopCount_RoundTrip_Executes() {
        var printed = CSharpLanguage.Write(new Return(new PopCount(new Constant(7L))));
        await Assert.That(printed).IsEqualTo("return System.Numerics.BitOperations.PopCount((ulong)7L);");

        var read = CSharpLanguage.Read("{ " + printed + " }");
        using var original = Interpreter.Execute(Interpreter.Compile(new PopCount(new Constant(7L))));
        await Assert.That(Execute(read)).IsEqualTo(Convert.ToInt64(original.Result.Value));
    }

    private static long Execute(CompilationUnitNode unit) {
        var program = CSharpScript.ProgramBlock(unit);
        var analysis = Interpreter.Analyze(unit);
        var errors = Errors(analysis);
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join("; ", errors));
        using var execution = Interpreter.Execute(Interpreter.Compile(program, analysis));
        return Convert.ToInt64(execution.Result.Value);
    }

    private static IReadOnlyList<string> Errors(AnalysisResult analysis) =>
        [.. analysis.Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.Message)];

    private static Node? RootOf(Node node) {
        var current = node;
        while (current is Member member)
            current = member.Value;
        return current;
    }

    private static T? Find<T>(Node root, Func<T, bool> predicate) where T : Node =>
        Walk(root).OfType<T>().FirstOrDefault(predicate);

    private static IEnumerable<Node> Walk(Node node) {
        yield return node;
        foreach (var child in node.Children) {
            if (child is null)
                continue;
            foreach (var descendant in Walk(child))
                yield return descendant;
        }
    }
}