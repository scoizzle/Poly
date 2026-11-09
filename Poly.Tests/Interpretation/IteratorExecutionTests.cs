using Poly.Interpretation;
using Poly.Interpretation.CSharp;
using Poly.Interpretation.Vm;

namespace Poly.Tests.Interpretation;

/// <summary>
/// #338 slice 2: an iterator method runs lazily on a private, resumable frame. The body resumes
/// through a hierarchical dispatch: an outer switch on the region id selects the block or loop
/// that owns the yield, and that region's own switch selects the yield to resume at.
/// </summary>
public class IteratorExecutionTests {
    private static async Task<string> Run(string script) {
        var result = CSharpScript.Run(script);
        await Assert.That(result.Diagnostics).IsEmpty();
        await Assert.That(result.Success).IsTrue();
        return result.Value!;
    }

    /// <summary>Runs <paramref name="script"/> on the VM and hands the <c>IEnumerable&lt;long&gt;</c>
    /// it evaluates to to the host, which drives it with plain C#.</summary>
    private static async Task<T> Host<T>(string script, Func<IEnumerable<long>, T> drive) {
        var unit = CSharpScript.ReadUnit(script);
        var analysis = Interpreter.Analyze(unit);
        await Assert.That(CSharpScript.Errors(analysis)).IsEmpty();
        var program = Interpreter.Compile(CSharpScript.ProgramBlock(unit), analysis, CompilationMode.Normal);
        using var exec = Interpreter.Execute(program);
        var sequence = exec.Result.Value as IEnumerable<long>;
        await Assert.That(sequence).IsNotNull();
        return drive(sequence!);
    }

    [Test]
    public async Task Iterator_YieldReturn_ProducesValues() {
        var value = await Run(
            "class C { C() { } IEnumerable<long> M() { yield return 3L; yield return 5L; yield return 7L; } "
            + "long R() { var s = 0L; var k = 0L; foreach (var x in this.M()) { k = k * 10 + x; s = s + 1; } return k * 10 + s; } } "
            + "new C().R();");
        await Assert.That(value).IsEqualTo("3573");
    }

    [Test]
    public async Task Iterator_ForeachConsumes() {
        var seen = await Host(
            "class C { C() { } IEnumerable<long> M() { yield return 2L; yield return 4L; yield return 6L; } } new C().M();",
            sequence => sequence.ToList());
        await Assert.That(seen).IsEquivalentTo(new long[] { 2, 4, 6 });
    }

    /// <summary>Nothing runs at the call; the first MoveNext runs to the first yield and stops;
    /// the second runs from there to the next yield. The counter is a field the body writes, read
    /// from the consuming loop between MoveNexts.</summary>
    [Test]
    public async Task Iterator_IsLazy() {
        var value = await Run(
            "class C { long n; C() { } "
            + "IEnumerable<long> M() { this.n = this.n + 1; yield return 10L; this.n = this.n + 10; yield return 20L; this.n = this.n + 100; } "
            + "long R() { var a = this.n; IEnumerable<long> e = this.M(); var b = this.n; long first = 0; long second = 0; long i = 0; "
            + "foreach (var x in e) { if (i == 0) { first = this.n; } if (i == 1) { second = this.n; } i = i + 1; } "
            + "return (((a * 1000 + b) * 1000 + first) * 1000 + second) * 1000 + this.n; } } "
            + "new C().R();");
        // a=0 before the call, b=0 after the call (nothing ran), first=1 while the first element is
        // being consumed (the body ran only to the first yield), second=11, and 111 once drained.
        await Assert.That(value).IsEqualTo("1011111");
    }

    [Test]
    public async Task Iterator_FirstMoveNext_RunsOnlyToTheFirstYield() {
        // break after the first element: the body past the first yield never runs.
        var value = await Run(
            "class C { long n; C() { } "
            + "IEnumerable<long> M() { this.n = this.n + 1; yield return 10L; this.n = this.n + 10; yield return 20L; } "
            + "long R() { foreach (var x in this.M()) { break; } return this.n; } } "
            + "new C().R();");
        await Assert.That(value).IsEqualTo("1");
    }

    [Test]
    public async Task Iterator_LoopState_PreservedAcrossYields() {
        var seen = await Host(
            "class C { C() { } IEnumerable<long> M() { long i = 0; long acc = 0; while (i < 4) { acc = acc + i * i; yield return acc; i = i + 1; } } } new C().M();",
            sequence => sequence.ToList());
        await Assert.That(seen).IsEquivalentTo(new long[] { 0, 1, 5, 14 });
    }

    [Test]
    public async Task Iterator_ParametersAndLocals_SurviveResume() {
        var seen = await Host(
            "class C { C() { } IEnumerable<long> M(long from, long to) { var step = 3L; var i = from; while (i < to) { yield return i; i = i + step; } yield return 99L; } } new C().M(1L, 10L);",
            sequence => sequence.ToList());
        await Assert.That(seen).IsEquivalentTo(new long[] { 1, 4, 7, 99 });
    }

    [Test]
    public async Task Iterator_YieldBreak_Stops() {
        var seen = await Host(
            "class C { C() { } IEnumerable<long> M() { long i = 0; while (true) { if (i == 3) { yield break; } yield return i; i = i + 1; } yield return 100L; } } new C().M();",
            sequence => sequence.ToList());
        await Assert.That(seen).IsEquivalentTo(new long[] { 0, 1, 2 });
    }

    [Test]
    public async Task Iterator_NestedRegions_ResumeIntoTheRightYield() {
        // Yields in the root block, in a loop body, and in two arms of an if inside it: four
        // regions, each resumed through its own switch.
        var seen = await Host(
            "class C { C() { } IEnumerable<long> M() { yield return 1L; long i = 0; while (i < 4) { if (i == 1) { yield return 10L; } else { yield return 20L; yield return 21L; } i = i + 1; } yield return 2L; } } new C().M();",
            sequence => sequence.ToList());
        await Assert.That(seen).IsEquivalentTo(new long[] { 1, 20, 21, 10, 20, 21, 20, 21, 2 });
    }

    [Test]
    public async Task Iterator_DoubleElements_RoundTrip() {
        var unit = CSharpScript.ReadUnit(
            "class C { C() { } IEnumerable<double> M() { yield return 1.5; yield return 2.25; } } new C().M();");
        var analysis = Interpreter.Analyze(unit);
        await Assert.That(CSharpScript.Errors(analysis)).IsEmpty();
        var program = Interpreter.Compile(CSharpScript.ProgramBlock(unit), analysis, CompilationMode.Normal);
        using var exec = Interpreter.Execute(program);
        var seen = ((IEnumerable<double>)exec.Result.Value!).ToList();
        await Assert.That(seen).IsEquivalentTo(new double[] { 1.5, 2.25 });
    }

    [Test]
    public async Task Iterator_MoveNextAfterExhaustion_False() {
        // A re-run of the body would yield n again, so a false here is the body not re-running.
        var moves = await Host(
            "class C { C() { } IEnumerable<long> M() { long n = 5; yield return n; } } new C().M();",
            sequence => {
                using var en = sequence.GetEnumerator();
                return new[] { en.MoveNext(), en.Current == 5, en.MoveNext(), en.MoveNext(), en.MoveNext() };
            });
        await Assert.That(moves).IsEquivalentTo(new[] { true, true, false, false, false });
    }

    [Test]
    public async Task Iterator_TwoLiveEnumerators_AreIndependent() {
        // GetEnumerator on one IEnumerable twice: each enumerator keeps its own position.
        var positions = await Host(
            "class C { C() { } IEnumerable<long> M() { yield return 1L; yield return 2L; yield return 3L; } } new C().M();",
            sequence => {
                using var a = sequence.GetEnumerator();
                using var b = sequence.GetEnumerator();
                a.MoveNext(); a.MoveNext(); b.MoveNext();
                var snapshot = new[] { a.Current, b.Current };
                a.MoveNext(); b.MoveNext();
                return new[] { snapshot[0], snapshot[1], a.Current, b.Current };
            });
        await Assert.That(positions).IsEquivalentTo(new long[] { 2, 1, 3, 2 });
    }

    [Test]
    public async Task Iterator_TwoLiveEnumerators_NestedForeachOverOneCall() {
        // The inner and outer loops enumerate separately but over the same call result; each is independent.
        var value = await Run(
            "class C { C() { } IEnumerable<long> M() { yield return 1L; yield return 2L; yield return 3L; } "
            + "long R() { IEnumerable<long> e = this.M(); long total = 0; foreach (var a in e) { foreach (var b in e) { total = total + a * b; } } return total; } } "
            + "new C().R();");
        // (1+2+3) * (1+2+3).
        await Assert.That(value).IsEqualTo("36");
    }

    /// <summary>The iterator's frame is private. While suspended, the body's spills must not sit
    /// on the shared stack, where a nested call (here <c>Burn</c>, with a frame as large as
    /// the stack region the iterator's frame would occupy) between two yields overwrites them.</summary>
    [Test]
    public async Task Iterator_PrivateFrame_SurvivesNestedCallsBetweenYields() {
        // Between yields the consumer calls Burn, whose frame lands on the shared stack exactly
        // where a frame left there by the iterator would sit. The iterator's locals p and q live
        // in spill slots; with a shared frame the nested call overwrites them.
        var value = await Run(
            "class C { C() { } "
            + "long Burn(long a, long b, long c, long d, long e, long f, long g) { var x = a + b; var y = c + d; var z = e + f + g + x + y; return z * 0 + 1; } "
            + "IEnumerable<long> M() { var p = 111L; var q = 222L; yield return p; var t = this.Burn(9L, 9L, 9L, 9L, 9L, 9L, 9L); yield return q + t; yield return p + q; } "
            + "long R() { long acc = 0; foreach (var v in this.M()) { var burn = this.Burn(7L, 7L, 7L, 7L, 7L, 7L, 7L); acc = acc * 1000 + v + burn * 0; } return acc; } } "
            + "new C().R();");
        // 111, 222 + 1, 111 + 222.
        await Assert.That(value).IsEqualTo("111223333");
    }
}