namespace Poly.Interpretation;

public sealed class TypedMetadataStore {
    private readonly Dictionary<Type, IAnalysisMetadata> _metadata = new();

    /// <summary>
    /// Initializes a new empty metadata store.
    /// </summary>
    public TypedMetadataStore() { }

    /// <summary>
    /// Initializes a new metadata store with data copied from another store.
    /// </summary>
    public TypedMetadataStore(TypedMetadataStore source)
    {
        ArgumentNullException.ThrowIfNull(source);
        foreach (var entry in source._metadata) {
            _metadata.Add(entry.Key, entry.Value);
        }
    }

    /// <summary>
    /// Retrieves all stored metadata instances.
    /// </summary>
    /// <returns>An enumerable of all metadata instances.</returns>
    public IEnumerable<IAnalysisMetadata> GetAll()
    {
        foreach (var entry in _metadata) {
            yield return entry.Value;
        }
    }

    /// <summary>
    /// Stores strongly-typed metadata contributed by middleware.
    /// Each middleware can define its own metadata type without coupling to others.
    /// </summary>
    /// <typeparam name="TMetadata">The metadata type to store.</typeparam>
    /// <param name="data">The metadata instance.</param>
    /// <exception cref="ArgumentNullException">Thrown when data is null.</exception>
    public void Set<TMetadata>(TMetadata data) where TMetadata : class, IAnalysisMetadata
    {
        ArgumentNullException.ThrowIfNull(data);
        _metadata.Add(typeof(TMetadata), data);
    }

    /// <summary>
    /// Retrieves strongly-typed metadata by type.
    /// </summary>
    /// <typeparam name="TMetadata">The metadata type to retrieve.</typeparam>
    /// <returns>The metadata instance if it exists; otherwise, null.</returns>
    public TMetadata? Get<TMetadata>() where TMetadata : class, IAnalysisMetadata
    {
        return _metadata.TryGetValue(typeof(TMetadata), out var data) ? (TMetadata)data : null;
    }


    /// <summary>
    /// Retrieves strongly-typed metadata by type.
    /// </summary>
    /// <typeparam name="TMetadata">The metadata type to retrieve.</typeparam>
    /// <returns>The metadata instance if it exists; otherwise, null.</returns>
    public TMetadata GetOrAdd<TMetadata>(Func<TMetadata> factory) where TMetadata : class, IAnalysisMetadata
    {
        if (!_metadata.TryGetValue(typeof(TMetadata), out var data)) {
            data = factory();
            _metadata.Add(typeof(TMetadata), data);
        }

        return (TMetadata)data;
    }

    /// <summary>
    /// Removes metadata of a given type.
    /// </summary>
    /// <typeparam name="TMetadata">The metadata type to remove.</typeparam>
    public void Remove<TMetadata>() where TMetadata : class, IAnalysisMetadata
    {
        _metadata.Remove(typeof(TMetadata));
    }
}