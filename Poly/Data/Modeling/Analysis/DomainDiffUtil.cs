using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

public sealed record DomainNodeChange(
    NodeId NodeId,
    string NodeType,
    string NodeName,
    string BeforeFingerprint,
    string AfterFingerprint,
    IReadOnlyList<Diagnostic> RelatedDiagnostics
);

public sealed record DomainNodeSnapshot(
    NodeId NodeId,
    string NodeType,
    string NodeName,
    string Fingerprint
);

public sealed record DomainSnapshot(
    string DomainName,
    IReadOnlyList<DomainNodeSnapshot> Nodes
);

public sealed record DomainDiffReport(
    IReadOnlyList<DomainNodeSnapshot> Added,
    IReadOnlyList<DomainNodeSnapshot> Removed,
    IReadOnlyList<DomainNodeChange> Changed
);

public static class DomainDiffUtil {
    public static DomainSnapshot CaptureSnapshot(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);

        var snapshot = SyntaxDiffUtil.CaptureSnapshot(
            root: domain,
            rootName: domain.Name,
            getNodeName: GetNodeName,
            buildFingerprint: BuildFingerprint,
            getChildren: static node => node.Children);

        return new DomainSnapshot(snapshot.RootName, snapshot.Nodes.Select(ToDomainSnapshot).ToArray());
    }

    public static DomainDiffReport Compare(Domain before, Domain after, AnalysisResult? analysis = null) {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var beforeSnapshot = CaptureSnapshot(before);
        var afterSnapshot = CaptureSnapshot(after);

        return CompareSnapshots(beforeSnapshot, afterSnapshot, analysis);
    }

    public static DomainDiffReport CompareSnapshots(DomainSnapshot before, DomainSnapshot after, AnalysisResult? analysis = null) {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var genericBefore = new NodeSnapshotSet(before.DomainName, before.Nodes.Select(ToGenericSnapshot).ToArray());
        var genericAfter = new NodeSnapshotSet(after.DomainName, after.Nodes.Select(ToGenericSnapshot).ToArray());
        var genericDiff = SyntaxDiffUtil.CompareSnapshots(genericBefore, genericAfter, analysis);

        return new DomainDiffReport(
            genericDiff.Added.Select(ToDomainSnapshot).ToArray(),
            genericDiff.Removed.Select(ToDomainSnapshot).ToArray(),
            genericDiff.Changed.Select(static change =>
                new DomainNodeChange(
                    change.NodeId,
                    change.NodeType,
                    change.NodeName,
                    change.BeforeFingerprint,
                    change.AfterFingerprint,
                    change.RelatedDiagnostics)).ToArray());
    }

    private static string GetNodeName(Node node) {
        if (node is not DomainObject domainNode) {
            return node.GetType().Name;
        }

        return domainNode switch {
            DomainMember member => member.Name,
            _ => domainNode.GetType().Name
        };
    }

    private static string BuildFingerprint(Node node) {
        if (node is not DomainObject domainNode) {
            return $"{node.GetType().Name}|{GetNodeName(node)}";
        }

        return BuildDomainFingerprint(domainNode);
    }

    private static string BuildDomainFingerprint(DomainObject node) {
        return node switch {
            Domain domain => $"Domain|{domain.Name}|objects:{domain.Objects.Count}",
            Primitive primitive => $"Primitive|{primitive.Name}|{primitive.Category}",
            Relationship relationship => string.Join('|', [
                "Relationship",
                relationship.Name,
                relationship.Source?.Id.Value ?? string.Empty,
                relationship.Target?.Id.Value ?? string.Empty,
                relationship.Cardinality.ToString(),
                relationship.SourceOwnsTarget.ToString()
            ]),
            Entity entity => string.Join('|', [
                "Entity",
                entity.Name,
                entity.ParentEntity?.Id.Value ?? string.Empty,
                $"props:{entity.Properties.Count}",
                $"stages:{entity.Stages.Count}",
                $"actions:{entity.Actions.Count}",
                $"events:{entity.Events.Count}",
                $"policies:{entity.Policies.Count}",
                $"rels:{entity.Relationships.Count}"
            ]),
            Stage stage => string.Join('|', [
                "Stage",
                stage.Name,
                stage.Parent?.Id.Value ?? string.Empty,
                stage.OwnerEntity?.Id.Value ?? string.Empty,
                $"actions:{stage.Actions.Count}",
                $"policies:{stage.Policies.Count}"
            ]),
            Action action => string.Join('|', [
                "Action",
                action.Name,
                action.Entity.Id.Value,
                $"params:{action.Parameters.Count}",
                $"effects:{action.Effects.Count}",
                $"policies:{action.Policies.Count}"
            ]),
            Event @event => $"Event|{@event.Name}|props:{@event.Properties.Count}",
            Property property => $"Property|{property.Name}|{property.Type.Id.Value}|policies:{property.Policies.Count}|constraints:{property.Constraints.Count}",
            Policy policy => $"Policy|{policy.Name}|rules:{policy.Rules.Count}|{policy.AggregationStrategy}",
            _ => $"{node.GetType().Name}|{GetNodeName(node)}"
        };
    }

    private static NodeSnapshot ToGenericSnapshot(DomainNodeSnapshot snapshot)
        => new(snapshot.NodeId, snapshot.NodeType, snapshot.NodeName, snapshot.Fingerprint);

    private static DomainNodeSnapshot ToDomainSnapshot(NodeSnapshot snapshot)
        => new(snapshot.NodeId, snapshot.NodeType, snapshot.NodeName, snapshot.Fingerprint);
}