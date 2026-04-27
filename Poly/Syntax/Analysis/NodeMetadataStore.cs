namespace Poly.Syntax.Analysis;

public sealed class NodeMetadataStore {
    private readonly Dictionary<(Poly.Syntax.AbstractSyntaxTree.NodeId, Type), IAnalysisMetadata> _metadata = new();

    public NodeMetadataStore() { }

    public NodeMetadataStore(NodeMetadataStore source) {
        ArgumentNullException.ThrowIfNull(source);
        foreach (var entry in source._metadata) {
            _metadata.Add(entry.Key, entry.Value);
        }
    }

    public void Set<TMetadata>(Poly.Syntax.AbstractSyntaxTree.Node node, TMetadata data) where TMetadata : class, IAnalysisMetadata {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(data);
        _metadata.Add((node.Id, typeof(TMetadata)), data);
    }

    public TMetadata? Get<TMetadata>(Poly.Syntax.AbstractSyntaxTree.Node node) where TMetadata : class, IAnalysisMetadata {
        ArgumentNullException.ThrowIfNull(node);
        return _metadata.TryGetValue((node.Id, typeof(TMetadata)), out var data) ? (TMetadata)data : null;
    }

    public IEnumerable<IAnalysisMetadata> GetAll(Poly.Syntax.AbstractSyntaxTree.Node node) {
        ArgumentNullException.ThrowIfNull(node);
        return _metadata
            .Where(kvp => kvp.Key.Item1 == node.Id)
            .Select(kvp => kvp.Value);
    }

    public TMetadata GetOrAdd<TMetadata>(Poly.Syntax.AbstractSyntaxTree.Node node, Func<TMetadata> factory) where TMetadata : class, IAnalysisMetadata {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(factory);

        if (!_metadata.TryGetValue((node.Id, typeof(TMetadata)), out var data)) {
            data = factory();
            _metadata.Add((node.Id, typeof(TMetadata)), data);
        }

        return (TMetadata)data;
    }

    public void Remove<TMetadata>(Poly.Syntax.AbstractSyntaxTree.Node node) where TMetadata : class, IAnalysisMetadata {
        ArgumentNullException.ThrowIfNull(node);
        _metadata.Remove((node.Id, typeof(TMetadata)));
    }
}