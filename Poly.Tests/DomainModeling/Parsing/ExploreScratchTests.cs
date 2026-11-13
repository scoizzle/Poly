namespace Poly.Tests.DomainModeling.Parsing;

public sealed class ExploreScratchTests {
    [Test]
    public async Task Dump() {
        var lines = new List<string> { "cells=" + RoundTripMatrixCells.All.Count, "keywordTargets=" + RoundTripMatrixCells.KeywordTargets.Count + ": " + string.Join(",", RoundTripMatrixCells.KeywordTargets) };
        foreach (var g in RoundTripMatrixCells.All.GroupBy(c => c.Dimension))
            lines.Add("dim " + g.Key + " " + g.Count());
        foreach (var c in RoundTripMatrixCells.All) {
            var o = RoundTripMatrixTests.Run(c);
            if (!o.Passed) lines.Add("FAIL " + c.Id + " " + o);
        }
        lines.Add("expectedFailures=" + RoundTripMatrixTests.ExpectedFailures().Count);
        lines.Add("universe=" + RoundTripMatrixTests.Universe().Count + " skips=" + RoundTripMatrixTests.Skips.Count);
        lines.Add("guard: " + string.Join("; ", RoundTripMatrixTests.GuardProblems(RoundTripMatrixTests.Universe(), RoundTripMatrixCells.All.SelectMany(c => c.Covers).ToHashSet(), RoundTripMatrixTests.Skips, 99)));
        lines.Add("annotations: " + string.Join(",", RoundTripMatrixTests.AnnotationKeywords()));
        try {
            var session = Poly.DomainModeling.Compile.DomainSession.ForExtensions(["temporal"]);
            var changes = new Poly.DomainModeling.Language.PolyDslParser("domain D\nJ: entity {\n  E: Duration default(12 Days)\n}\n", session).Parse();
            var r = new Poly.DomainModeling.Evolution.DomainEvolution(new Poly.DomainModeling.Ontology.Domain("_", [])).Apply(changes, session: session);
            lines.Add("durationDefault parsed " + r.Succeeded + " " + string.Join(";", r.MutationErrors));
            lines.Add(new Poly.DomainModeling.Language.DomainDslPrinter(session).Print(r.Root));
        } catch (Exception ex) { lines.Add("durationDefault EX " + ex.GetType().Name + " " + ex.Message); }
        File.WriteAllLines("/private/tmp/claude-501/-Users-scoizzle--t3-worktrees-Poly-t3-84dbe9d2-b1bf4f67f44d3ab9/7eb5eb36-cc84-4a61-8002-a0b24bb6481a/scratchpad/cells.txt", lines);
        await Assert.That(lines.Count).IsGreaterThan(0);
    }
}
