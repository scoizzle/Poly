using Poly.Interpretation;

namespace Poly.Tests.Interpretation;

/// <summary>
/// #374 S1: the seeded AST-shape generator, run through the Roslyn differential. The oracle is the
/// printed C# compiled by Roslyn; the generator predicts nothing. A failure names its seed and index, and
/// <c>AstShapeGenerator.Generate(seed)[index]</c> reproduces it.
/// </summary>
public class AstShapeFuzzTests {
    static readonly IReadOnlyList<AstShapeGenerator.GeneratedCase> DefaultSet = AstShapeGenerator.Generate(AstShapeGenerator.DefaultSeed);

    static IReadOnlyList<(AstShapeGenerator.GeneratedCase Case, ParityRow Row)> Run(IEnumerable<AstShapeGenerator.GeneratedCase> cases) =>
        [.. cases.Select(c => (c, AstParity.Compare(c.Label, c.Program)))];

    [Test]
    public async Task Generate_SameSeedTwice_ProducesIdenticalPrograms() {
        var first = AstShapeGenerator.Generate(AstShapeGenerator.DefaultSeed);
        var second = AstShapeGenerator.Generate(AstShapeGenerator.DefaultSeed);

        await Assert.That(AstShapeGenerator.Render(second)).IsEqualTo(AstShapeGenerator.Render(first));
        // The text is a rendering the generator chose; the printed C# is the program itself.
        var printedFirst = string.Join("\n---\n", first.Select(c => AstParity.Print(c.Program)));
        var printedSecond = string.Join("\n---\n", second.Select(c => AstParity.Print(c.Program)));
        await Assert.That(printedSecond).IsEqualTo(printedFirst);
    }

    [Test]
    public async Task Generate_DifferentSeeds_ProduceDifferentPrograms() {
        var a = AstShapeGenerator.Render(AstShapeGenerator.Generate(1));
        var b = AstShapeGenerator.Render(AstShapeGenerator.Generate(2));
        await Assert.That(a).IsNotEqualTo(b);
        // Differing seeds differ in program text, not only in labels.
        var bodiesA = AstShapeGenerator.Generate(1).Select(c => c.Text).ToHashSet();
        var bodiesB = AstShapeGenerator.Generate(2).Select(c => c.Text).ToHashSet();
        await Assert.That(bodiesA.Overlaps(bodiesB)).IsFalse().Because("two seeds should not share programs");
    }

    [Test]
    public async Task Generate_OneCase_ReproducesFromSeedAndIndexAlone() {
        var small = AstShapeGenerator.Generate(AstShapeGenerator.DefaultSeed, count: 8);
        await Assert.That(small[7].Text).IsEqualTo(DefaultSet[7].Text);
        await Assert.That(small[7].Label).IsEqualTo(DefaultSet[7].Label);
    }

    [Test]
    public async Task Generate_IsBounded() {
        await Assert.That(DefaultSet.Count).IsEqualTo(AstShapeGenerator.DefaultCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => AstShapeGenerator.Generate(1, AstShapeGenerator.MaxCount + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => AstShapeGenerator.Generate(1, 1, AstShapeGenerator.MaxDepth + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => AstShapeGenerator.Generate(1, -1));
        // Depth bound: no program nests deeper than the block + MaxDepth expression levels.
        await Assert.That(AstShapeGenerator.Generate(1, 50, AstShapeGenerator.MaxDepth)
            .Max(c => Depth(c.Text))).IsLessThanOrEqualTo(AstShapeGenerator.MaxDepth + 1);
    }

    static int Depth(string text) {
        var depth = 0;
        var max = 0;
        foreach (var ch in text.Substring(text.LastIndexOf(';') + 1)) {
            if (ch == '(') max = Math.Max(max, ++depth);
            else if (ch == ')') depth--;
        }
        return max;
    }

    [Test]
    public async Task Generate_RecordsWhatItDoesNotEmit() {
        await Assert.That(AstShapeGenerator.NotGenerated).IsNotEmpty();
        await Assert.That(AstShapeGenerator.NotGenerated.All(e => e.Why.Length >= 20)).IsTrue();
    }

    [Test]
    public async Task DefaultSeed_PrintedCSharp_AgreesWithTheVm() {
        var rows = Run(DefaultSet);

        var disagreements = rows.Where(r => !r.Row.Agrees && !AstParity.KnownDivergences.Any(d => d.Matches(r.Row)))
            .Select(r => r.Row.Describe()).ToList();
        await Assert.That(disagreements).IsEmpty().Because(string.Join("\n\n", disagreements.Take(3)));
    }

    [Test]
    public async Task DefaultSeed_Verdict_IsNotVacuous() {
        var rows = Run(DefaultSet);

        var bothExecuted = rows.Count(r => r.Row.Vm == ParityDisposal.Executed && r.Row.Printed == ParityDisposal.Executed);
        var bothRejected = rows.Count(r => r.Row.RejectedOnBothSides);
        var throws = rows.Count(r => r.Row.VmOutcome.StartsWith("throw ", StringComparison.Ordinal));
        var distinct = rows.Select(r => r.Row.VmOutcome).Distinct().Count();

        // A harness that rejects everything on both sides "agrees" with everything.
        await Assert.That(bothExecuted).IsGreaterThanOrEqualTo(AstShapeGenerator.DefaultCount * 9 / 10)
            .Because($"executed on both sides {bothExecuted}/{rows.Count}, rejected on both {bothRejected}");
        await Assert.That(distinct).IsGreaterThan(20).Because($"only {distinct} distinct outcomes");
        await Assert.That(throws).IsLessThan(rows.Count / 2).Because($"{throws} throws; shapes should mostly produce values");
    }

    [Test]
    public async Task Harness_CatchesADeliberatelyWrongCase() {
        // The writer erases SuspendNode and prints its child while the VM signals "suspend": a real,
        // already-recorded asymmetry. It stands in for "a writer arm was mutated" without touching the writer.
        var wrong = new AstShapeGenerator.GeneratedCase("injected#wrong", new SuspendNode(new Constant(7L)), "SuspendNode(7)");
        var cases = DefaultSet.Take(10).Append(wrong).ToList();

        var rows = Run(cases);
        var disagreements = rows.Where(r => !r.Row.Agrees).Select(r => r.Case.Label).ToList();

        await Assert.That(disagreements).IsEquivalentTo(["injected#wrong"]);
    }

    [Test]
    public async Task Verdict_OnASyntheticMismatch_IsNotAgreement() {
        var row = new ParityRow("synthetic", ParityDisposal.Executed, "long:3", ParityDisposal.Executed, "long:4", null);
        await Assert.That(row.Agrees).IsFalse();
        var oneSideRefuses = row with { Printed = ParityDisposal.Rejected, PrintedOutcome = "writer refused: x" };
        await Assert.That(oneSideRefuses.Agrees).IsFalse();
    }
}