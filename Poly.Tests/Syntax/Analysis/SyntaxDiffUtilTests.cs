namespace Poly.Tests.Syntax.Analysis;

public class SyntaxDiffUtilTests {
    [Test]
    public async Task Compare_WhenNodeAddedRemovedAndChanged_ReportsAllCategories() {
        var sharedRootId = NodeId.NewId();
        var sharedChildId = NodeId.NewId();
        var removedId = NodeId.NewId();
        var addedId = NodeId.NewId();

        var before = new TestBranch(
            Name: "root",
            ChildrenList: [
                new TestLeaf("shared", 1) { Id = sharedChildId },
                new TestLeaf("removed", 10) { Id = removedId }
            ]) { Id = sharedRootId };

        var after = new TestBranch(
            Name: "root",
            ChildrenList: [
                new TestLeaf("shared", 2) { Id = sharedChildId },
                new TestLeaf("added", 20) { Id = addedId }
            ]) { Id = sharedRootId };

        var diff = SyntaxDiffUtil.Compare(before, after, GetNodeName, BuildFingerprint);

        await Assert.That(diff.Added.Any(node => node.NodeId == addedId)).IsTrue();
        await Assert.That(diff.Removed.Any(node => node.NodeId == removedId)).IsTrue();
        await Assert.That(diff.Changed.Any(node => node.NodeId == sharedChildId)).IsTrue();
    }

    [Test]
    public async Task CompareSnapshots_WithAnalysis_AttachesDiagnosticsToChangedNodes() {
        var sharedRootId = NodeId.NewId();
        var changedId = NodeId.NewId();

        var before = new TestBranch(
            Name: "root",
            ChildrenList: [new TestLeaf("shared", 1) { Id = changedId }]) { Id = sharedRootId };

        var afterLeaf = new TestLeaf("shared", 2) { Id = changedId };
        var after = new TestBranch(
            Name: "root",
            ChildrenList: [afterLeaf]) { Id = sharedRootId };

        var beforeSnapshot = SyntaxDiffUtil.CaptureSnapshot(before, GetNodeName, BuildFingerprint);
        var afterSnapshot = SyntaxDiffUtil.CaptureSnapshot(after, GetNodeName, BuildFingerprint);

        var context = new AnalysisContext(Poly.Introspection.CommonLanguageRuntime.ClrTypeDefinitionRegistry.Shared);
        context.ReportWarning(afterLeaf, "changed leaf", "DIFF001");
        var analysis = new AnalysisResult(context);

        var diff = SyntaxDiffUtil.CompareSnapshots(beforeSnapshot, afterSnapshot, analysis);
        var changed = diff.Changed.Single(entry => entry.NodeId == changedId);

        await Assert.That(changed.RelatedDiagnostics.Count).IsEqualTo(1);
        await Assert.That(changed.RelatedDiagnostics[0].Code).IsEqualTo("DIFF001");
    }

    private static string GetNodeName(Node node) => node switch {
        TestLeaf leaf => leaf.Name,
        TestBranch branch => branch.Name,
        _ => node.GetType().Name
    };

    private static string BuildFingerprint(Node node) => node switch {
        TestLeaf leaf => $"Leaf|{leaf.Name}|{leaf.Value}",
        TestBranch branch => $"Branch|{branch.Name}|children:{branch.ChildrenList.Count}",
        _ => node.GetType().Name
    };

    private sealed record TestLeaf(string Name, int Value) : Node;

    private sealed record TestBranch(string Name, IReadOnlyList<Node> ChildrenList) : Node {
        public override IEnumerable<Node?> Children => ChildrenList;
    }
}