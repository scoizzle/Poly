using Poly.Interpretation;
using Poly.Interpretation.CSharp;
using Poly.Interpretation.Vm;

namespace Poly.Tests.Interpretation;

/// <summary>
/// #338 slice 4: the iterator differential. The VM drains the lazy sequence a Poly iterator returns
/// and Roslyn drains the C# iterator the writer prints for the same unit; the yielded values must
/// match in order and count. This is the AstParity oracle for <see cref="YieldStatement"/>.
/// </summary>
public class IteratorParityTests {
    public static IEnumerable<(string Label, string Script)> Programs() {
        yield return ("straight_line",
            "public class C { public C() { } public IEnumerable<long> M() { yield return 3L; yield return 5L; yield return 7L; } } new C().M();");
        yield return ("yield_break_stops",
            "public class C { public C() { } public IEnumerable<long> M() { long i = 0; while (true) { if (i == 3) { yield break; } yield return i; i = i + 1; } } } new C().M();");
        yield return ("loop_state_across_yields",
            "public class C { public C() { } public IEnumerable<long> M() { long i = 0; long acc = 0; while (i < 4) { acc = acc + i * i; yield return acc; i = i + 1; } } } new C().M();");
        yield return ("nested_regions",
            "public class C { public C() { } public IEnumerable<long> M() { yield return 1L; long i = 0; while (i < 4) { if (i == 1) { yield return 10L; } else { yield return 20L; yield return 21L; } i = i + 1; } yield return 2L; } } new C().M();");
        yield return ("yield_in_try_finally",
            "public class C { public C() { } public IEnumerable<long> M() { long i = 0; try { while (i < 3) { yield return i; i = i + 1; } } finally { i = 100; } yield return i; } } new C().M();");
        yield return ("parameterless_empty",
            "public class C { public C() { } public IEnumerable<long> M() { yield break; } } new C().M();");
    }

    [Test]
    [MethodDataSource(nameof(Programs))]
    public async Task Iterator_PrintedCSharp_YieldsWhatTheVmYields((string Label, string Script) program) {
        var row = AstParity.CompareIterator("Iterator." + program.Label, program.Script, "C", "M");
        await Assert.That(row.Vm).IsEqualTo(ParityDisposal.Executed).Because(row.Describe());
        await Assert.That(row.Printed).IsEqualTo(ParityDisposal.Executed).Because(row.Describe());
        await Assert.That(row.Agrees).IsTrue().Because(row.Describe());
    }

    /// <summary>Anti-vacuity: a constant-returning harness would agree on every program above.</summary>
    [Test]
    public async Task NegativeControl_TheYieldedValuesAreComputedOnBothSides() {
        var one = AstParity.CompareIterator("one", Programs().First(p => p.Label == "straight_line").Script, "C", "M");
        var two = AstParity.CompareIterator("two", Programs().First(p => p.Label == "yield_break_stops").Script, "C", "M");
        await Assert.That(one.VmOutcome).IsNotEqualTo(two.VmOutcome);
        await Assert.That(one.PrintedOutcome).IsNotEqualTo(two.PrintedOutcome);
        await Assert.That(one.VmOutcome).IsEqualTo("seq[long:3,long:5,long:7]");
        await Assert.That(one.PrintedOutcome).IsEqualTo("seq[long:3,long:5,long:7]");
    }
}