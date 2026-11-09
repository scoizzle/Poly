using Poly.Interpretation;
using Poly.Interpretation.CSharp;
using Poly.Interpretation.Vm;

namespace Poly.Tests.Interpretation;

/// <summary>
/// #338 slice 2: an iterator method runs lazily on a private, resumable frame. The body resumes
/// through a hierarchical dispatch: an outer switch on the region id selects the block
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

    /// <summary>The idiomatic form: an implicitly-typed local holding an iterator call result.</summary>
    [Test]
    public async Task Iterator_VarLocal_HoldsTheIteratorAndEnumerates() {
        var value = await Run(
            "class C { C() { } IEnumerable<long> M() { yield return 1L; yield return 2L; } "
            + "long R() { var e = this.M(); long s = 0; foreach (var x in e) { s = s + x; } return s; } } "
            + "new C().R();");
        await Assert.That(value).IsEqualTo("3");
    }

    private static async Task<List<long>> Drain(string script) => await Host(script, s => s.Take(8).ToList());

    private static async Task<string> FirstError(string script) {
        var unit = CSharpScript.ReadUnit(script);
        var analysis = Interpreter.Analyze(unit);
        var errors = CSharpScript.Errors(analysis);
        await Assert.That(errors).IsNotEmpty();
        return errors.First();
    }

    /// <summary>Fix 1 chose fail-closed for list-only members: the provider spells IEnumerable&lt;T&gt; as
    /// List&lt;T&gt;, so before this rule <c>var e = M(); e.Count</c> type-checked and threw
    /// InvalidCastException (VmIterator to List) at run time. Enumeration through <c>var</c> stays legal.</summary>
    [Test]
    public async Task Iterator_VarLocal_MemberAccess_IsRejectedAtAnalysis() {
        var code = await FirstError(
            "class C { C() { } IEnumerable<long> M() { yield return 1L; } long R() { var e = this.M(); return e.Count; } } new C().R();");
        await Assert.That(code).StartsWith("IT0007");
    }

    [Test]
    public async Task Iterator_ExplicitLocal_MemberCall_IsRejectedAtAnalysis() {
        var code = await FirstError(
            "class C { C() { } IEnumerable<long> M() { yield return 1L; } long R() { IEnumerable<long> e = this.M(); e.Add(5L); return 0; } } new C().R();");
        await Assert.That(code).StartsWith("IT0007");
    }

    [Test]
    public async Task Iterator_MoveNextAfterExecDispose_Throws() {
        var unit = CSharpScript.ReadUnit(
            "class C { C() { } IEnumerable<long> M() { yield return 1L; yield return 2L; } } new C().M();");
        var analysis = Interpreter.Analyze(unit);
        var program = Interpreter.Compile(CSharpScript.ProgramBlock(unit), analysis, CompilationMode.Normal);
        var exec = Interpreter.Execute(program);
        var sequence = (IEnumerable<long>)exec.Result.Value!;
        var en = sequence.GetEnumerator();
        await Assert.That(en.MoveNext()).IsTrue();
        exec.Dispose();
        await Assert.That(() => en.MoveNext()).Throws<ObjectDisposedException>();
        await Assert.That(() => sequence.GetEnumerator()).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task Iterator_BareReturn_EndsLikeYieldBreak() {
        var seen = await Drain(
            "class C { C() { } IEnumerable<long> M() { yield return 1L; yield return 2L; return; yield return 3L; } } new C().M();");
        await Assert.That(seen).IsEquivalentTo(new long[] { 1, 2 });
    }

    [Test]
    public async Task Iterator_BareStatementYield_UnderIf() {
        var seen = await Drain(
            "class C { C() { } IEnumerable<long> M() { long i = 0; while (i < 4) { if (i % 2 == 0) yield return i; i = i + 1; } } } new C().M();");
        await Assert.That(seen).IsEquivalentTo(new long[] { 0, 2 });
    }

    [Test]
    public async Task Iterator_BareStatementYield_UnderWhile() {
        var seen = await Drain(
            "class C { C() { } IEnumerable<long> M() { long i = 0; while (i < 3) yield return 7L; yield return 9L; } } new C().M();");
        // The bare-statement body re-yields while the condition holds; Drain stops at 8.
        await Assert.That(seen).IsEquivalentTo(new long[] { 7, 7, 7, 7, 7, 7, 7, 7 });
    }

    [Test]
    public async Task Iterator_ThrowMidBody_ThenMoveNext_FalseNoHang() {
        var unit = CSharpScript.ReadUnit(
            "class C { C() { } IEnumerable<long> M() { yield return 1L; throw new System.InvalidOperationException(); yield return 2L; } } new C().M();");
        var analysis = Interpreter.Analyze(unit);
        await Assert.That(CSharpScript.Errors(analysis)).IsEmpty();
        var program = Interpreter.Compile(CSharpScript.ProgramBlock(unit), analysis, CompilationMode.Normal);
        using var exec = Interpreter.Execute(program);
        using var en = ((IEnumerable<long>)exec.Result.Value!).GetEnumerator();
        await Assert.That(en.MoveNext()).IsTrue();
        var threw = false;
        try { en.MoveNext(); } catch (Exception) { threw = true; }
        await Assert.That(threw).IsTrue();
        await Assert.That(en.MoveNext()).IsFalse();
    }

    [Test]
    public async Task Iterator_NonGenericIEnumerable_IsRejectedAtCompile() {
        var result = CSharpScript.Run(
            "class C { C() { } IEnumerable M() { yield return 1L; } } new C().M();");
        await Assert.That(result.Success).IsFalse();
        await Assert.That(string.Join("|", result.Diagnostics)).Contains("IEnumerable");
    }

    [Test]
    public async Task Iterator_BlockLocal_InLoop_SurvivesYield() {
        var seen = await Drain(
            "class C { C() { } IEnumerable<long> M() { long i = 0; while (i < 3) { { long inner = i * 10; yield return inner; yield return inner + 1; } i = i + 1; } } } new C().M();");
        await Assert.That(seen).IsEquivalentTo(new long[] { 0, 1, 10, 11, 20, 21 });
    }

    // ── #338 slice 3: disposal and nested constructs ───────────────────────────────

    private const string FinallyClass =
        "class C { long n; C() { } "
        + "IEnumerable<long> M() { try { yield return 1L; yield return 2L; yield return 3L; } finally { this.n = this.n + 1; } } ";

    [Test]
    public async Task Iterator_Dispose_RunsFinally() {
        // foreach disposes its enumerator on break: the outstanding finally runs, once.
        var value = await Run(FinallyClass
            + "long R() { foreach (var x in this.M()) { break; } return this.n; } } new C().R();");
        await Assert.That(value).IsEqualTo("1");
    }

    [Test]
    public async Task Iterator_Exhaustion_RunsFinallyOnce_NotAgainOnDispose() {
        // The body completes: the finally runs there. foreach then disposes the exhausted enumerator.
        var value = await Run(FinallyClass
            + "long R() { long s = 0; foreach (var x in this.M()) { s = s + x; } return this.n * 100 + s; } } new C().R();");
        await Assert.That(value).IsEqualTo("106");
    }

    [Test]
    public async Task Iterator_NeverStarted_Dispose_RunsNoFinally() {
        var value = await Run(FinallyClass
            + "long R() { foreach (var x in this.M()) { break; } var p = this.n; foreach (var y in this.M()) { break; } return p * 10 + this.n; } } new C().R();");
        // Each loop started its iterator and disposed it once: 1 then 2.
        await Assert.That(value).IsEqualTo("12");
    }

    [Test]
    public async Task Iterator_YieldBreakInTryFinally_StillRunsFinally() {
        var value = await Run(
            "class C { long n; C() { } "
            + "IEnumerable<long> M() { try { yield return 1L; yield break; } finally { this.n = this.n + 1; } } "
            + "long R() { long s = 0; foreach (var x in this.M()) { s = s + x; } return this.n * 10 + s; } } new C().R();");
        await Assert.That(value).IsEqualTo("11");
    }

    [Test]
    public async Task Iterator_FinallyReadsLocal_AfterDispose() {
        var value = await Run(
            "class C { long n; C() { } "
            + "IEnumerable<long> M() { long k = 7L; try { yield return k; yield return k + 1; } finally { this.n = k * 3; } } "
            + "long R() { foreach (var x in this.M()) { break; } return this.n; } } new C().R();");
        await Assert.That(value).IsEqualTo("21");
    }

    [Test]
    public async Task Iterator_NestedTryFinally_DisposeRunsInnerThenOuter() {
        var value = await Run(
            "class C { long n; C() { } "
            + "IEnumerable<long> M() { try { try { yield return 1L; yield return 2L; } finally { this.n = this.n * 10 + 1; } } finally { this.n = this.n * 10 + 2; } } "
            + "long R() { foreach (var x in this.M()) { break; } return this.n; } } new C().R();");
        await Assert.That(value).IsEqualTo("12");
    }

    [Test]
    public async Task Iterator_FinallyAfterNormalCompletion_RunsBeforeLaterStatements() {
        var seen = await Drain(
            "class C { long n; C() { } "
            + "IEnumerable<long> M() { try { yield return 1L; } finally { this.n = 5L; } yield return this.n; } } new C().M();");
        await Assert.That(seen).IsEquivalentTo(new long[] { 1, 5 });
    }

    [Test]
    public async Task Iterator_ThrowInsideTry_RunsFinally() {
        var value = await Run(
            "class C { long n; C() { } "
            + "IEnumerable<long> M() { try { yield return 1L; throw new System.InvalidOperationException(); } finally { this.n = this.n + 1; } } "
            + "long R() { try { foreach (var x in this.M()) { } } catch (System.InvalidOperationException e) { } return this.n; } } new C().R();");
        await Assert.That(value).IsEqualTo("1");
    }

    [Test]
    public async Task Iterator_Using_DisposesOnEarlyBreak() {
        var value = await Run(
            "class C { System.IO.MemoryStream s; C() { } "
            + "IEnumerable<long> M() { this.s = new System.IO.MemoryStream(); using (this.s) { yield return 1L; yield return 2L; } } "
            + "long R() { foreach (var x in this.M()) { break; } if (this.s.CanRead) { return 0; } return 1; } } new C().R();");
        await Assert.That(value).IsEqualTo("1");
    }

    [Test]
    public async Task Iterator_Using_DisposesOnExhaustion() {
        var value = await Run(
            "class C { System.IO.MemoryStream s; C() { } "
            + "IEnumerable<long> M() { this.s = new System.IO.MemoryStream(); using (this.s) { yield return 1L; yield return 2L; } } "
            + "long R() { foreach (var x in this.M()) { } if (this.s.CanRead) { return 0; } return 1; } } new C().R();");
        await Assert.That(value).IsEqualTo("1");
    }

    [Test]
    public async Task Iterator_UsingInsideFinally_DisposesInnerFirst() {
        var value = await Run(
            "class C { long n; System.IO.MemoryStream s; C() { } "
            + "IEnumerable<long> M() { this.s = new System.IO.MemoryStream(); try { using (this.s) { yield return 1L; yield return 2L; } } "
            + "finally { if (this.s.CanRead) { this.n = 1; } else { this.n = 2; } } } "
            + "long R() { foreach (var x in this.M()) { break; } return this.n; } } new C().R();");
        // The using is inner: it has disposed the stream by the time the outer finally reads it.
        await Assert.That(value).IsEqualTo("2");
    }

    [Test]
    public async Task Iterator_ForeachInside_YieldsAll() {
        var seen = await Drain(
            "class C { C() { } IEnumerable<long> Inner() { yield return 1L; yield return 2L; } "
            + "IEnumerable<long> M() { foreach (var a in this.Inner()) { yield return a * 10; yield return a + 100; } yield return 99L; } } new C().M();");
        await Assert.That(seen).IsEquivalentTo(new long[] { 10, 101, 20, 102, 99 });
    }

    [Test]
    public async Task Iterator_ForeachInside_EarlyDispose_DisposesInnerEnumerator() {
        // The inner iterator's own finally runs when the outer iterator is disposed mid-foreach.
        var value = await Run(
            "class C { long n; C() { } "
            + "IEnumerable<long> Inner() { try { yield return 1L; yield return 2L; } finally { this.n = this.n + 1; } } "
            + "IEnumerable<long> M() { foreach (var a in this.Inner()) { yield return a; } } "
            + "long R() { foreach (var x in this.M()) { break; } return this.n; } } new C().R();");
        await Assert.That(value).IsEqualTo("1");
    }

    [Test]
    public async Task Iterator_ForLoopWithYield_ProducesValues() {
        var seen = await Drain(
            "class C { C() { } IEnumerable<long> M() { for (long i = 0; i < 4; i = i + 1) { yield return i * i; } yield return 100L; } } new C().M();");
        await Assert.That(seen).IsEquivalentTo(new long[] { 0, 1, 4, 9, 100 });
    }

    [Test]
    public async Task Iterator_WhileInsideTryFinally_YieldsAndCleansUp() {
        var value = await Run(
            "class C { long n; C() { } "
            + "IEnumerable<long> M() { long i = 0; try { while (i < 5) { yield return i; i = i + 1; } } finally { this.n = this.n + i + 10; } } "
            + "long R() { foreach (var x in this.M()) { if (x == 2) { break; } } return this.n; } } new C().R();");
        // Disposed with i == 2.
        await Assert.That(value).IsEqualTo("12");
    }

    [Test]
    public async Task Iterator_BreakOutOfLoopInsideTryFinally_RunsNoEarlyFinally() {
        // A break whose loop is inside the try does not leave the try: the finally runs once, at its end.
        var seen = await Drain(
            "class C { long n; C() { } "
            + "IEnumerable<long> M() { try { long i = 0; while (true) { if (i == 2) { break; } yield return i; i = i + 1; } } finally { this.n = 8L; } yield return this.n; } } new C().M();");
        await Assert.That(seen).IsEquivalentTo(new long[] { 0, 1, 8 });
    }

    [Test]
    public async Task Iterator_BreakCrossingTryFinallyWithYield_IsRejected() {
        var result = CSharpScript.Run(
            "class C { long n; C() { } "
            + "IEnumerable<long> M() { long i = 0; while (true) { try { yield return i; if (i == 2) { break; } i = i + 1; } finally { this.n = 1L; } } } } new C().M();");
        await Assert.That(result.Success).IsFalse();
        await Assert.That(string.Join("|", result.Diagnostics)).Contains("IT0008");
    }

    [Test]
    public async Task Iterator_YieldInTryWithCatch_StaysRejected() {
        var error = await FirstError(
            "class C { C() { } IEnumerable<long> M() { try { yield return 1L; } catch (System.Exception e) { } } } new C().M();");
        await Assert.That(error).Contains("IT0004");
    }

    /// <summary>Runs <paramref name="script"/> to a list of sequences over one object: the first is the
    /// iterator under test, the second reads the counter its finally increments.</summary>
    private static async Task<(IEnumerable<long> Sequence, IEnumerable<long> Probe)> HostPair(string script) {
        var unit = CSharpScript.ReadUnit(script);
        var analysis = Interpreter.Analyze(unit);
        await Assert.That(CSharpScript.Errors(analysis)).IsEmpty();
        var program = Interpreter.Compile(CSharpScript.ProgramBlock(unit), analysis, CompilationMode.Normal);
        var exec = Interpreter.Execute(program);
        var items = (System.Collections.IList)exec.Result.Value!;
        return ((IEnumerable<long>)items[0]!, (IEnumerable<long>)items[1]!);
    }

    private const string PairScript =
        "class C { long n; C() { } "
        + "IEnumerable<long> M() { try { yield return 1L; yield return 2L; yield return 3L; } finally { this.n = this.n + 1; } } "
        + "IEnumerable<long> Probe() { yield return this.n; } } "
        + "class D { D() { } System.Collections.ArrayList Pair() { var c = new C(); var l = new System.Collections.ArrayList(); l.Add(c.M()); l.Add(c.Probe()); return l; } } "
        + "new D().Pair();";

    [Test]
    public async Task Iterator_Dispose_Twice_RunsFinallyOnce() {
        var (sequence, probe) = await HostPair(PairScript);
        using var en = sequence.GetEnumerator();
        await Assert.That(en.MoveNext()).IsTrue();
        await Assert.That(probe.Single()).IsEqualTo(0L);
        en.Dispose();
        await Assert.That(probe.Single()).IsEqualTo(1L);
        en.Dispose();
        await Assert.That(probe.Single()).IsEqualTo(1L);
        // A disposed enumerator is finished.
        await Assert.That(en.MoveNext()).IsFalse();
    }

    [Test]
    public async Task Iterator_ExhaustedThenDisposed_RunsFinallyOnce() {
        var (sequence, probe) = await HostPair(PairScript);
        var en = sequence.GetEnumerator();
        while (en.MoveNext()) { }
        await Assert.That(probe.Single()).IsEqualTo(1L);
        en.Dispose();
        en.Dispose();
        await Assert.That(probe.Single()).IsEqualTo(1L);
    }

    [Test]
    public async Task Iterator_DisposedBeforeFirstMoveNext_RunsNoFinally() {
        var (sequence, probe) = await HostPair(PairScript);
        var en = sequence.GetEnumerator();
        en.Dispose();
        await Assert.That(probe.Single()).IsEqualTo(0L);
        await Assert.That(en.MoveNext()).IsFalse();
    }

    [Test]
    public async Task Iterator_TwoEnumerators_DisposeEachRunsItsOwnFinallyOnce() {
        var (sequence, probe) = await HostPair(PairScript);
        var a = sequence.GetEnumerator();
        var b = sequence.GetEnumerator();
        a.MoveNext(); b.MoveNext();
        a.Dispose();
        await Assert.That(probe.Single()).IsEqualTo(1L);
        b.Dispose();
        a.Dispose();
        b.Dispose();
        await Assert.That(probe.Single()).IsEqualTo(2L);
    }

    [Test]
    public async Task Iterator_GotoInsideTryFinallyWithYield_IsRejected() {
        var result = CSharpScript.Run(
            "class C { long n; C() { } "
            + "IEnumerable<long> M() { try { yield return 1L; goto done; } finally { this.n = 1L; } done: yield return 2L; } } new C().M();");
        await Assert.That(result.Success).IsFalse();
        await Assert.That(string.Join("|", result.Diagnostics)).Contains("IT0009");
    }

    [Test]
    public async Task Iterator_ContinueInsideForeachWithYield_StaysInsideTheRegion() {
        var result = CSharpScript.Run(
            "class C { C() { } IEnumerable<long> Inner() { yield return 1L; } "
            + "IEnumerable<long> M() { long i = 0; while (i < 3) { i = i + 1; foreach (var a in this.Inner()) { yield return a; continue; } } } } new C().M();");
        // continue targets the foreach itself, so it does not leave the region.
        await Assert.That(result.Success).IsTrue();
    }

    [Test]
    public async Task Iterator_ContinueOutOfUsingWithYield_IsRejected() {
        var result = CSharpScript.Run(
            "class C { System.IO.MemoryStream s; C() { } "
            + "IEnumerable<long> M() { long i = 0; while (i < 3) { i = i + 1; using (new System.IO.MemoryStream()) { yield return i; continue; } } } } new C().M();");
        await Assert.That(result.Success).IsFalse();
        await Assert.That(string.Join("|", result.Diagnostics)).Contains("IT0008");
    }

    [Test]
    public async Task Iterator_TryFinallyInLoop_CleansEachIteration_AndTheDisposedOne() {
        var value = await Run(
            "class C { long n; C() { } "
            + "IEnumerable<long> M() { long i = 0; while (i < 4) { try { yield return i; } finally { this.n = this.n + 1; } i = i + 1; } } "
            + "long R() { foreach (var x in this.M()) { if (x == 1) { break; } } return this.n; } } new C().R();");
        // Iteration 0 completed (1), iteration 1 was disposed mid-try (2).
        await Assert.That(value).IsEqualTo("2");
    }

    [Test]
    public async Task Iterator_ReturnInTryFinally_StillRunsFinally() {
        var value = await Run(
            "class C { long n; C() { } "
            + "IEnumerable<long> M() { try { yield return 1L; return; } finally { this.n = this.n + 1; } } "
            + "long R() { long s = 0; foreach (var x in this.M()) { s = s + x; } return this.n * 10 + s; } } new C().R();");
        await Assert.That(value).IsEqualTo("11");
    }
}