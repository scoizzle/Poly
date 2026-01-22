namespace Poly.Interpretation;

public sealed class InterpretationMetadataStore {
    private readonly ConditionalWeakTable<Type, object> _metadata = new();

    /// <summary>
    /// Stores strongly-typed metadata contributed by middleware.
    /// Each middleware can define its own metadata type without coupling to others.
    /// </summary>
    /// <typeparam name="TMetadata">The metadata type to store.</typeparam>
    /// <param name="data">The metadata instance.</param>
    /// <exception cref="ArgumentNullException">Thrown when data is null.</exception>
    public void Set<TMetadata>(TMetadata data) where TMetadata : class
    {
        ArgumentNullException.ThrowIfNull(data);
        _metadata.Add(typeof(TMetadata), data);
    }

    /// <summary>
    /// Retrieves strongly-typed metadata by type.
    /// </summary>
    /// <typeparam name="TMetadata">The metadata type to retrieve.</typeparam>
    /// <returns>The metadata instance if it exists; otherwise, null.</returns>
    public TMetadata? Get<TMetadata>() where TMetadata : class
    {
        return _metadata.TryGetValue(typeof(TMetadata), out var data) ? (TMetadata)data : null;
    }


    /// <summary>
    /// Retrieves strongly-typed metadata by type.
    /// </summary>
    /// <typeparam name="TMetadata">The metadata type to retrieve.</typeparam>
    /// <returns>The metadata instance if it exists; otherwise, null.</returns>
    public TMetadata GetOrAdd<TMetadata>(Func<TMetadata> factory) where TMetadata : class
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
    public void Remove<TMetadata>() where TMetadata : class
    {
        _metadata.Remove(typeof(TMetadata));
    }
}
