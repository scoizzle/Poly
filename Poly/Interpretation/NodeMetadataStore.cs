namespace Poly.Interpretation;

public sealed class NodeMetadataStore {
    private readonly Dictionary<(NodeId, Type), IAnalysisMetadata> _metadata = new();

    /// <summary>
    /// Initializes a new empty metadata store.
    /// </summary>
    public NodeMetadataStore() { }

    /// <summary>
    /// Initializes a new metadata store with data copied from another store.
    /// </summary>
    public NodeMetadataStore(NodeMetadataStore source)
    {
        ArgumentNullException.ThrowIfNull(source);
        foreach (var entry in source._metadata) {
            _metadata.Add(entry.Key, entry.Value);
        }
    }

    /// <summary>
    /// Stores strongly-typed metadata contributed by middleware.
    /// Each middleware can define its own metadata type without coupling to others.
    /// </summary>
    /// <typeparam name="TMetadata">The metadata type to store.</typeparam>
    /// <param name="data">The metadata instance.</param>
    /// <exception cref="ArgumentNullException">Thrown when data is null.</exception>
    public void Set<TMetadata>(Node node, TMetadata data) where TMetadata : class, IAnalysisMetadata
    {
        ArgumentNullException.ThrowIfNull(data);
        _metadata.Add((node.Id, typeof(TMetadata)), data);
    }

    /// <summary>
    /// Retrieves strongly-typed metadata by type.
    /// </summary>
    /// <typeparam name="TMetadata">The metadata type to retrieve.</typeparam>
    /// <returns>The metadata instance if it exists; otherwise, null.</returns>
    public TMetadata? Get<TMetadata>(Node node) where TMetadata : class, IAnalysisMetadata
    {
        return _metadata.TryGetValue((node.Id, typeof(TMetadata)), out var data) ? (TMetadata)data : null;
    }

    /// <summary>
    /// Retrieves all metadata attached to a node.
    /// </summary>
    /// <param name="node">The node to query.</param>
    /// <returns>All metadata instances for the node.</returns>
    public IEnumerable<IAnalysisMetadata> GetAll(Node node)
    {
        return _metadata
            .Where(kvp => kvp.Key.Item1 == node.Id)
            .Select(kvp => kvp.Value);
    }


    /// <summary>
    /// Retrieves strongly-typed metadata by type.
    /// </summary>
    /// <typeparam name="TMetadata">The metadata type to retrieve.</typeparam>
    /// <returns>The metadata instance if it exists; otherwise, null.</returns>
    public TMetadata GetOrAdd<TMetadata>(Node node, Func<TMetadata> factory) where TMetadata : class, IAnalysisMetadata
    {
        if (!_metadata.TryGetValue((node.Id, typeof(TMetadata)), out var data)) {
            data = factory();
            _metadata.Add((node.Id, typeof(TMetadata)), data);
        }

        return (TMetadata)data;
    }

    /// <summary>
    /// Removes metadata of a given type.
    /// </summary>
    /// <typeparam name="TMetadata">The metadata type to remove.</typeparam>
    public void Remove<TMetadata>(Node node) where TMetadata : class, IAnalysisMetadata
    {
        _metadata.Remove((node.Id, typeof(TMetadata)));
    }
}