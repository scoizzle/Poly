using Poly.Interpretation;
using Poly.Interpretation.CSharp;
using Poly.Interpretation.Vm;

namespace Poly.Tests.Interpretation;

/// <summary>
/// #338 slice 4: a yield is a statement boundary like any other, so the debug hook fires on it each
/// time the body runs to it. The shared seam has no suspension pause for an iterator:
/// <see cref="VmDebugger"/> pauses on <see cref="InterpreterStatus.Suspended"/>, which is the root
/// delegate leaving at a <see cref="SuspendNode"/>, while an iterator yields inside
/// <c>VmIterator.MoveNext</c> on the consumer's thread and never sets that status. That pause is
/// deferred to #337's diagnostics slice, as the issue directs.
/// </summary>
public class IteratorDebuggerTests {
    [Test]
    public async Task DebugHook_FiresOnEachYieldStatement_AsTheConsumerDrivesTheIterator() {
        var unit = CSharpScript.ReadUnit(
            "class C { C() { } IEnumerable<long> M() { yield return 2L; yield return 4L; yield break; } } new C().M();");
        var analysis = Interpreter.Analyze(unit);
        var program = Interpreter.Compile(CSharpScript.ProgramBlock(unit), analysis, CompilationMode.Normal);

        var yields = new List<Node>();
        using var exec = Interpreter.Execute(program, state => state.DebugHook = (node, _, _) => {
            if (node is YieldStatement)
                yields.Add(node);
        });
        var sequence = (IEnumerable<long>)exec.Result.Value!;
        await Assert.That(yields).IsEmpty().Because("nothing in the iterator body has run before the first MoveNext");

        var seen = new List<long>();
        var hooked = new List<int>();
        foreach (var value in sequence) {
            seen.Add(value);
            hooked.Add(yields.Count);
        }

        await Assert.That(seen).IsEquivalentTo(new long[] { 2, 4 });
        await Assert.That(hooked).IsEquivalentTo(new[] { 1, 2 })
            .Because("the hook fires on the yield that produced each element, before MoveNext returns it");
        await Assert.That(yields.Count).IsEqualTo(3).Because("the closing yield break is a boundary too");
        await Assert.That(yields.All(node => node is YieldStatement)).IsTrue();
    }

    [Test]
    public async Task NoDebug_EmitsNoHookOnAYield() {
        var unit = CSharpScript.ReadUnit(
            "class C { C() { } IEnumerable<long> M() { yield return 2L; } } new C().M();");
        var analysis = Interpreter.Analyze(unit);
        var program = Interpreter.Compile(CSharpScript.ProgramBlock(unit), analysis, CompilationMode.NoDebug);
        var fired = 0;
        using var exec = Interpreter.Execute(program, state => state.DebugHook = (_, _, _) => fired++);
        _ = ((IEnumerable<long>)exec.Result.Value!).ToList();
        await Assert.That(fired).IsEqualTo(0);
    }
}