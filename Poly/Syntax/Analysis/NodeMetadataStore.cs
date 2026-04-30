namespace Poly.Syntax.Analysis;

/// <summary>
/// Stores analysis metadata keyed by node identity and metadata type.
/// Uses a two-level layout (NodeId → per-node bucket) so that invalidating a node is O(1)
/// regardless of how many other nodes are in the store.
/// Each per-node bucket stores up to <see cref="InlineCapacity"/> entries in a flat array
/// (linear scan, cache-friendly) and promotes to a dictionary only when that threshold is exceeded.
/// <see cref="Set{TMetadata}"/> uses overwrite semantics so that incremental reruns of the same
/// analysis pass are idempotent.
/// </summary>
public sealed class NodeMetadataStore {
    private const int InlineCapacity = 4;

    private readonly Dictionary<NodeId, NodeBucket> _buckets = new();

    public NodeMetadataStore() { }

    public NodeMetadataStore(NodeMetadataStore source) {
        ArgumentNullException.ThrowIfNull(source);
        foreach (var (id, bucket) in source._buckets) {
            _buckets[id] = bucket.Clone();
        }
    }

    public void Set<TMetadata>(Node node, TMetadata data) where TMetadata : class, IAnalysisMetadata {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(data);
        GetOrCreateBucket(node.Id).Set(typeof(TMetadata), data);
    }

    public TMetadata? Get<TMetadata>(Node node) where TMetadata : class, IAnalysisMetadata {
        ArgumentNullException.ThrowIfNull(node);
        return _buckets.TryGetValue(node.Id, out var bucket)
            ? bucket.Get(typeof(TMetadata)) as TMetadata
            : null;
    }

    public IEnumerable<IAnalysisMetadata> GetAll(Node node) {
        ArgumentNullException.ThrowIfNull(node);
        return _buckets.TryGetValue(node.Id, out var bucket) ? bucket.GetAll() : [];
    }

    public TMetadata GetOrAdd<TMetadata>(Node node, Func<TMetadata> factory) where TMetadata : class, IAnalysisMetadata {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(factory);
        return (TMetadata)GetOrCreateBucket(node.Id).GetOrAdd(typeof(TMetadata), () => factory());
    }

    public void Remove<TMetadata>(Node node) where TMetadata : class, IAnalysisMetadata {
        ArgumentNullException.ThrowIfNull(node);
        if (_buckets.TryGetValue(node.Id, out var bucket)) {
            bucket.Remove(typeof(TMetadata));
        }
    }

    /// <summary>
    /// Removes all metadata for <paramref name="node"/> in O(1).
    /// </summary>
    public void RemoveAll(Node node) {
        ArgumentNullException.ThrowIfNull(node);
        _buckets.Remove(node.Id);
    }

    /// <summary>
    /// Removes all metadata for the specified node id in O(1).
    /// </summary>
    public void RemoveAll(NodeId nodeId) {
        _buckets.Remove(nodeId);
    }

    private NodeBucket GetOrCreateBucket(NodeId id) {
        if (!_buckets.TryGetValue(id, out var bucket)) {
            bucket = new NodeBucket();
            _buckets[id] = bucket;
        }

        return bucket;
    }

    /// <summary>
    /// Per-node metadata container. Stores up to <see cref="InlineCapacity"/> entries using
    /// parallel inline arrays (no heap allocation per entry) and promotes to a dictionary
    /// only when that limit is exceeded.
    /// </summary>
    private sealed class NodeBucket {
        private Type[]? _keys;
        private IAnalysisMetadata[]? _values;
        private int _count;
        private Dictionary<Type, IAnalysisMetadata>? _overflow;

        public void Set(Type type, IAnalysisMetadata data) {
            if (_overflow is not null) {
                _overflow[type] = data;
                return;
            }

            for (var i = 0; i < _count; i++) {
                if (_keys![i] == type) {
                    _values![i] = data;
                    return;
                }
            }

            if (_count < InlineCapacity) {
                if (_keys is null) {
                    _keys = new Type[InlineCapacity];
                    _values = new IAnalysisMetadata[InlineCapacity];
                }

                _keys[_count] = type;
                _values![_count] = data;
                _count++;
            }
            else {
                _overflow = new Dictionary<Type, IAnalysisMetadata>(_count + 1, ReferenceEqualityComparer.Instance);
                for (var i = 0; i < _count; i++) {
                    _overflow[_keys![i]] = _values![i];
                }

                _overflow[type] = data;
                _keys = null;
                _values = null;
                _count = 0;
            }
        }

        public IAnalysisMetadata? Get(Type type) {
            if (_overflow is not null) {
                return _overflow.TryGetValue(type, out var v) ? v : null;
            }

            for (var i = 0; i < _count; i++) {
                if (_keys![i] == type) return _values![i];
            }

            return null;
        }

        public IAnalysisMetadata GetOrAdd(Type type, Func<IAnalysisMetadata> factory) {
            var existing = Get(type);
            if (existing is not null) return existing;

            var created = factory();
            Set(type, created);
            return created;
        }

        public void Remove(Type type) {
            if (_overflow is not null) {
                _overflow.Remove(type);
                return;
            }

            for (var i = 0; i < _count; i++) {
                if (_keys![i] != type) continue;

                _count--;
                for (var j = i; j < _count; j++) {
                    _keys[j] = _keys[j + 1];
                    _values![j] = _values[j + 1];
                }

                _keys[_count] = null!;
                _values![_count] = null!;
                return;
            }
        }

        public IAnalysisMetadata[] GetAll() {
            if (_overflow is not null) return [.. _overflow.Values];
            if (_count == 0) return [];

            var result = new IAnalysisMetadata[_count];
            Array.Copy(_values!, result, _count);
            return result;
        }

        public NodeBucket Clone() {
            var clone = new NodeBucket();

            if (_overflow is not null) {
                clone._overflow = new Dictionary<Type, IAnalysisMetadata>(_overflow, ReferenceEqualityComparer.Instance);
            }
            else if (_count > 0) {
                clone._keys = (Type[])_keys!.Clone();
                clone._values = (IAnalysisMetadata[])_values!.Clone();
                clone._count = _count;
            }

            return clone;
        }
    }
}