namespace Poly.Analysis;

using Poly.Extensions;
/// <summary>
/// Stores analysis metadata keyed by node identity and metadata type.
/// Uses a two-level layout (NodeId → per-node bucket) so clearing a node is O(1)
/// regardless of how many other nodes are in the store.
/// Each per-node bucket stores up to <see cref="InlineCapacity"/> entries in a flat array
/// (linear scan, cache-friendly) and promotes to a dictionary only when that threshold is exceeded.
/// <see cref="Set{TMetadata}"/> overwrites the same metadata type on a node.
///
/// <para><b>Global (non-node) metadata.</b>
/// Passing <c>null</c> for the <c>node</c> parameter stores the metadata under
/// <see cref="NodeId.Empty"/>, which acts as a sentinel for pass-level or
/// analysis-level data that isn't associated with any single AST node.
/// <c>Get</c> falls back to the <c>NodeId.Empty</c> bucket when a per-node
/// lookup misses.  This is used, for example, by the lowering pipeline to
/// accumulate heap-allocated constant values during µop generation without
/// attaching them to a particular AST node.</para>
///
/// One store is owned by one <see cref="AnalysisContext"/>; each <see cref="Analyzer.Analyze"/>
/// allocates a new context. The store is not shared across runs or threads.
/// </summary>
public sealed class NodeMetadataStore : INodeMetadataProvider {
    private const int InlineCapacity = 4;

    private readonly Dictionary<NodeId, NodeBucket> _buckets = [];

    public NodeMetadataStore() { }

    public NodeMetadataStore(NodeMetadataStore source) {
        ArgumentNullException.ThrowIfNull(source);
        foreach (var (id, bucket) in source._buckets) {
            _buckets[id] = bucket.Clone();
        }
    }

    public void Set<TMetadata>(Node? node, TMetadata data) where TMetadata : class, IAnalysisMetadata {
        ArgumentNullException.ThrowIfNull(data);
        GetOrCreateBucket(node?.Id ?? NodeId.Empty).Set(typeof(TMetadata), data);
    }

    public TMetadata? Get<TMetadata>(Node? node) where TMetadata : class, IAnalysisMetadata {
        NodeBucket? bucket;
        if (node is not null) {
            if (_buckets.TryGetValue(node.Id, out bucket) && bucket.Get(typeof(TMetadata)) is TMetadata metadata) {
                return metadata;
            }
        }

        if (_buckets.TryGetValue(NodeId.Empty, out bucket) && bucket.Get(typeof(TMetadata)) is TMetadata globalMetadata) {
            return globalMetadata;
        }
        return null;
    }

    public IEnumerable<IAnalysisMetadata> GetAll(Node? node) {
        return _buckets.TryGetValue(node?.Id ?? NodeId.Empty, out var bucket) ? bucket.GetAll() : [];
    }

    public TMetadata GetOrAdd<TMetadata>(Node? node, Func<TMetadata> factory) where TMetadata : class, IAnalysisMetadata {
        ArgumentNullException.ThrowIfNull(factory);
        return (TMetadata)GetOrCreateBucket(node?.Id ?? NodeId.Empty).GetOrAdd(typeof(TMetadata), factory);
    }

    public void Remove<TMetadata>(Node? node) where TMetadata : class, IAnalysisMetadata {
        if (_buckets.TryGetValue(node?.Id ?? NodeId.Empty, out var bucket)) {
            bucket.Remove(typeof(TMetadata));
        }
    }

    /// <summary>
    /// Removes all metadata for <paramref name="node"/> in O(1).
    /// </summary>
    public void RemoveAll(Node? node) {
        _buckets.Remove(node?.Id ?? NodeId.Empty);
    }

    /// <summary>
    /// Removes all metadata for the specified node id in O(1).
    /// </summary>
    public void RemoveAll(NodeId nodeId) {
        _buckets.Remove(nodeId);
    }

    private NodeBucket GetOrCreateBucket(NodeId id) {
        return _buckets.GetOrAdd(id, static _ => new NodeBucket());
    }

    TMetadata? INodeMetadataProvider.GetMetadata<TMetadata>(Node? node) where TMetadata : class => Get<TMetadata>(node);

    /// <summary>
    /// Per-node metadata container. Stores up to <see cref="InlineCapacity"/> entries using
    /// parallel inline arrays (no heap allocation per entry) and promotes to a dictionary
    /// only when that limit is exceeded.
    /// </summary>
    private sealed class NodeBucket {
        private int _count;
        private (Type _keys, IAnalysisMetadata _values)[]? _inline;
        private Dictionary<Type, IAnalysisMetadata>? _overflow;

        public void Set(Type type, IAnalysisMetadata data) {
            if (_overflow is not null) {
                _overflow[type] = data;
                _count = _overflow.Count;
                return;
            }

            for (var i = 0; i < _count; i++) {
                if (_inline![i]._keys == type) {
                    _inline![i]._values = data;
                    return;
                }
            }

            if (_count < InlineCapacity) {
                if (_inline is null) {
                    _inline = new (Type _keys, IAnalysisMetadata _values)[InlineCapacity];
                }

                _inline[_count]._keys = type;
                _inline[_count]._values = data;
                _count++;
            }
            else {
                Debug.Assert(_inline is not null);

                _overflow = new Dictionary<Type, IAnalysisMetadata>(_count + 1, ReferenceEqualityComparer.Instance);
                foreach (var (key, value) in _inline) {
                    _overflow[key] = value;
                }

                _overflow[type] = data;
                _count = _overflow.Count;
                _inline = null;
            }
        }

        public IAnalysisMetadata? Get(Type type) {
            if (_count == 0) return default;

            if (_inline is not null) {
                Debug.Assert(_count <= _inline.Length);

                foreach (var (key, value) in _inline) {
                    if (key == type) return value;
                }

                return default;
            }

            Debug.Assert(_overflow is not null);
            _overflow.TryGetValue(type, out var result);
            return result;
        }

        public T GetOrAdd<T>(Type type, Func<T> factory) where T : IAnalysisMetadata {
            var existing = Get(type);

            if (existing is null) {
                var created = factory();
                Set(type, created);
                return created;
            }

            if (existing is not T typed)
                throw new InvalidOperationException($"Existing metadata type does not match requested type. Existing: {existing.GetType().FullName}, Requested: {typeof(T).FullName}");

            return typed;
        }

        public void Remove(Type type) {
            if (_inline is not null) {
                for (var i = 0; i < _count; i++) {
                    var (key, _) = _inline[i];
                    if (key != type) continue;

                    Array.Copy(_inline, i + 1, _inline, i, _count - i - 1);
                    _count--;
                    _inline[_count] = default;
                    return;
                }
            }
            else {
                Debug.Assert(_overflow is not null);
                _overflow.Remove(type);
                _count = _overflow.Count;
            }
        }

        public IAnalysisMetadata[] GetAll() {
            if (_count == 0) return [];

            if (_inline is not null) {
                var result = new IAnalysisMetadata[_count];
                for (var i = 0; i < _count; i++) {
                    result[i] = _inline[i]._values;
                }
                return result;
            }

            Debug.Assert(_overflow is not null);
            return [.. _overflow.Values];
        }

        public NodeBucket Clone() {
            var clone = new NodeBucket();

            if (_overflow is not null) {
                clone._overflow = new Dictionary<Type, IAnalysisMetadata>(_overflow, ReferenceEqualityComparer.Instance);
                clone._count = _count;
            }
            else if (_count > 0) {
                clone._inline = new (Type _keys, IAnalysisMetadata _values)[InlineCapacity];
                Array.Copy(_inline!, clone._inline, _count);
                clone._count = _count;
            }

            return clone;
        }
    }
}