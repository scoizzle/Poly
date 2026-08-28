using System.Collections.Concurrent;

namespace Poly.Analysis;

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
/// </summary>
public sealed class NodeMetadataStore {
    private const int InlineCapacity = 4;

    private readonly ConcurrentDictionary<NodeId, NodeBucket> _buckets = new();

    public NodeMetadataStore() { }

    public NodeMetadataStore(NodeMetadataStore source) {
        ArgumentNullException.ThrowIfNull(source);
        // Snapshot clone: each bucket is duplicated under its own lock so the copy is
        // consistent per bucket, even if another thread is mutating the source
        // concurrently. Buckets are never replaced once added, so iterating the
        // concurrent table while cloning is safe.
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
        _buckets.TryRemove(node?.Id ?? NodeId.Empty, out _);
    }

    /// <summary>
    /// Removes all metadata for the specified node id in O(1).
    /// </summary>
    public void RemoveAll(NodeId nodeId) {
        _buckets.TryRemove(nodeId, out _);
    }

    private NodeBucket GetOrCreateBucket(NodeId id) {
        return _buckets.GetOrAdd(id, static _ => new NodeBucket());
    }

    /// <summary>
    /// Per-node metadata container. Stores up to <see cref="InlineCapacity"/> entries using
    /// parallel inline arrays (no heap allocation per entry) and promotes to a dictionary
    /// only when that limit is exceeded.
    /// All mutations are guarded by <see cref="_lock"/> so a single store can be shared by
    /// concurrently-running analysis passes. Reads stay lock-free until a bucket promotes to
    /// its overflow dictionary — the common inline path only touches that bucket's own
    /// retained arrays, and the overflow path (five or more distinct metadata types) is
    /// rare enough that taking the lock keeps readers consistent with writers.
    /// </summary>
    private sealed class NodeBucket {
        private readonly Lock _lock = new();
        private int _count;
        private (Type _keys, IAnalysisMetadata _values)[]? _inline;
        private Dictionary<Type, IAnalysisMetadata>? _overflow;

        public void Set(Type type, IAnalysisMetadata data) {
            using var scope = _lock.EnterScope();

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
            using var scope = _lock.EnterScope();

            if (_count == 0) return default;

            if (_inline is not null) {
                foreach (var (key, value) in _inline) {
                    if (key == type) return value;
                }

                return default;
            }
            else {
                Debug.Assert(_overflow is not null);
                _overflow.TryGetValue(type, out var result);
                return result;
            }
        }

        public IAnalysisMetadata GetOrAdd(Type type, Func<IAnalysisMetadata> factory) {
            using var scope = _lock.EnterScope();
            var existing = Get(type);
            if (existing is not null) return existing;

            var created = factory();
            Set(type, created);
            return created;
        }

        public void Remove(Type type) {
            using var scope = _lock.EnterScope();

            if (_inline is not null) {
                for (var i = 0; i < _count; i++) {
                    var (key, _) = _inline[i];
                    if (key != type) continue;

                    Array.Copy(_inline, i + 1, _inline, i, _count - i - 1);
                    _count--;
                    // Clear the vacated trailing slot so a removed metadata instance is
                    // promptly eligible for collection instead of lingering for the
                    // bucket's lifetime.
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
            using var scope = _lock.EnterScope();

            if (_inline is not null) {
                var result = new IAnalysisMetadata[_count];
                for (var i = 0; i < _count; i++) {
                    result[i] = _inline[i]._values;
                }
                return result;
            }
            else {
                Debug.Assert(_overflow is not null);
                return _overflow.Values.ToArray();
            }
        }

        public NodeBucket Clone() {
            using var _ = _lock.EnterScope();
            var clone = new NodeBucket();

            if (_overflow is not null) {
                clone._overflow = new Dictionary<Type, IAnalysisMetadata>(_overflow, ReferenceEqualityComparer.Instance);
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