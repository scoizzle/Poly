using Poly.Interpretation;
using Poly.Interpretation.CSharp;
using Poly.Interpretation.Vm;
using Poly.Interpretation.Vm.Abi;

namespace Poly.Tests.Interpretation;

/// <summary>
/// #338 slice 1: <c>yield return</c> / <c>yield break</c> read, print and analyze, and the VM
/// fails closed. Legality is the C# rule set: only in an <c>IEnumerable</c> / <c>IEnumerable&lt;T&gt;</c>
/// body, never in a lambda, a finally, a catch, or a try that has a catch, and the yielded value
/// converts to the element type.
/// </summary>
public class IteratorYieldTests {
    private const string Valid =
        "class C { IEnumerable<long> M() { yield return 1L; yield break; } }";

    private static CompilationUnitNode Read(string text) => CSharpLanguage.Read(text);

    private static string[] Codes(string text) =>
        [.. Interpreter.Analyze(Read(text)).Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Code is not null && d.Code.StartsWith("IT", StringComparison.Ordinal))
            .Select(d => d.Code!)];

    private static IEnumerable<Node> Walk(Node node) => NodeFactCorpus.Walk(node);

    [Test]
    public async Task Iterator_RoundTrips() {
        var unit = Read(Valid);
        var yields = Walk(unit).OfType<YieldStatement>().ToArray();
        await Assert.That(yields.Length).IsEqualTo(2);
        await Assert.That(yields[0].Value is Constant).IsTrue();
        await Assert.That(yields[1].Value).IsNull();

        var printed = CSharpLanguage.Write(unit);
        await Assert.That(printed).Contains("yield return 1L;");
        await Assert.That(printed).Contains("yield break;");
        var again = CSharpLanguage.Write(Read(printed));
        await Assert.That(again).IsEqualTo(printed);
    }

    [Test]
    public async Task Iterator_Valid_AnalyzesWithoutIteratorDiagnostics() {
        await Assert.That(Codes(Valid)).IsEmpty();
    }

    [Test]
    public async Task Iterator_NonGenericIEnumerable_AnyValue_Accepted() {
        await Assert.That(Codes("class C { IEnumerable M() { yield return 1L; } }")).IsEmpty();
    }

    [Test]
    public async Task Iterator_YieldInFinally_Rejected() {
        var codes = Codes("class C { IEnumerable<long> M() { try { var a = 1L; } finally { yield return 1L; } } }");
        await Assert.That(codes).Contains("IT0003");
    }

    [Test]
    public async Task Iterator_YieldInTryWithCatch_Rejected() {
        var codes = Codes("class C { IEnumerable<long> M() { try { yield return 1L; } catch (Exception) { var a = 1L; } } }");
        await Assert.That(codes).Contains("IT0004");
    }

    [Test]
    public async Task Iterator_YieldInCatch_Rejected() {
        var codes = Codes("class C { IEnumerable<long> M() { try { var a = 1L; } catch (Exception) { yield return 1L; } } }");
        await Assert.That(codes).Contains("IT0005");
    }

    [Test]
    public async Task Iterator_YieldInTryFinallyWithoutCatch_Accepted() {
        var codes = Codes("class C { IEnumerable<long> M() { try { yield return 1L; } finally { var a = 1L; } } }");
        await Assert.That(codes).IsEmpty();
    }

    [Test]
    public async Task Iterator_YieldInVoidMethod_Rejected() {
        var codes = Codes("class C { void M() { yield return 1L; } }");
        await Assert.That(codes).Contains("IT0001");
    }

    [Test]
    public async Task Iterator_YieldInNonIteratorReturnType_Rejected() {
        var codes = Codes("class C { long M() { yield return 1L; } }");
        await Assert.That(codes).Contains("IT0001");
    }

    [Test]
    public async Task Iterator_YieldBreakInVoidMethod_Rejected() {
        var codes = Codes("class C { void M() { yield break; } }");
        await Assert.That(codes).Contains("IT0001");
    }

    [Test]
    public async Task Iterator_YieldInLambda_Rejected() {
        var codes = Codes("class C { IEnumerable<long> M() { var f = (long v) => { yield return v; }; yield break; } }");
        await Assert.That(codes).Contains("IT0002");
    }

    [Test]
    public async Task Iterator_YieldAtScriptRoot_Rejected() {
        var diagnostics = Interpreter.Analyze(new Block([new YieldStatement()])).Diagnostics;
        await Assert.That(diagnostics.Any(d => d.Code == "IT0001" && d.Severity == DiagnosticSeverity.Error)).IsTrue();
        await Assert.That(() => Interpreter.Compile(new Block([new YieldStatement()])))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Iterator_YieldValueTypeMismatch_Rejected() {
        var codes = Codes("class C { IEnumerable<long> M() { yield return \"s\"; } }");
        await Assert.That(codes).Contains("IT0006");
    }

    [Test]
    public async Task Iterator_YieldValueConvertible_Accepted() {
        // int widens to long, exactly as C# converts the yielded expression.
        var codes = Codes("class C { IEnumerable<long> M() { yield return 1; } }");
        await Assert.That(codes).IsEmpty();
    }

    [Test]
    public async Task Iterator_Vm_FailsClosed_OnYieldNode() {
        var node = new YieldStatement(new Constant(1L));
        var analysis = Interpreter.Analyze(node);
        var thrown = await Assert.That(() => AbiEmitter.Build(node, analysis, CompilationMode.NoDebug))
            .Throws<InvalidOperationException>();
        await Assert.That(thrown!.Message).Contains("yield");
    }

    [Test]
    public async Task Iterator_Vm_FailsClosed_OnYieldBreakNode() {
        var node = new YieldStatement();
        var analysis = Interpreter.Analyze(node);
        var thrown = await Assert.That(() => AbiEmitter.Build(node, analysis, CompilationMode.NoDebug))
            .Throws<InvalidOperationException>();
        await Assert.That(thrown!.Message).Contains("yield");
    }
}