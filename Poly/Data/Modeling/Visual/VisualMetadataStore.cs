using System.Collections.Immutable;

using Poly.Syntax;

namespace Poly.Data.Modeling.Visual;

/// <summary>
/// Stores visual layout metadata for domain objects, keyed by NodeId.
/// Immutable operations: modifications return new stores with updated state.
/// Designed for efficient round-trip integrity: visual state can be persisted and restored without loss.
/// </summary>
public sealed class VisualMetadataStore {
    private readonly ImmutableDictionary<NodeId, VisualLayout> _layouts;

    private VisualMetadataStore(ImmutableDictionary<NodeId, VisualLayout> layouts) {
        _layouts = layouts ?? ImmutableDictionary<NodeId, VisualLayout>.Empty;
    }

    /// <summary>Creates a new empty visual metadata store.</summary>
    public VisualMetadataStore() : this(ImmutableDictionary<NodeId, VisualLayout>.Empty) { }

    /// <summary>Gets the visual layout for a node, or a default layout if not set.</summary>
    public VisualLayout GetLayout(NodeId nodeId) =>
        _layouts.TryGetValue(nodeId, out var layout) ? layout : VisualLayout.AtOrigin();

    /// <summary>Checks if a node has custom visual layout defined.</summary>
    public bool HasLayout(NodeId nodeId) => _layouts.ContainsKey(nodeId);

    /// <summary>Returns the count of nodes with visual layout metadata.</summary>
    public int Count => _layouts.Count;

    /// <summary>Returns all node IDs with custom layout metadata.</summary>
    public IEnumerable<NodeId> AllNodeIds => _layouts.Keys;

    /// <summary>Sets the visual layout for a node, returning a new store.</summary>
    public VisualMetadataStore SetLayout(NodeId nodeId, VisualLayout layout) {
        ArgumentNullException.ThrowIfNull(layout);
        return new(_layouts.SetItem(nodeId, layout));
    }

    /// <summary>Updates a node's layout by applying a transformation function, returning a new store.</summary>
    public VisualMetadataStore UpdateLayout(NodeId nodeId, Func<VisualLayout, VisualLayout> transform) {
        ArgumentNullException.ThrowIfNull(transform);
        var current = GetLayout(nodeId);
        var updated = transform(current);
        return new(_layouts.SetItem(nodeId, updated));
    }

    /// <summary>Moves a node to a new position, returning a new store.</summary>
    public VisualMetadataStore MoveNode(NodeId nodeId, double x, double y) =>
        UpdateLayout(nodeId, layout => layout.MoveTo(x, y));

    /// <summary>Resizes a node, returning a new store.</summary>
    public VisualMetadataStore ResizeNode(NodeId nodeId, double width, double height) =>
        UpdateLayout(nodeId, layout => layout.Resize(width, height));

    /// <summary>Removes visual layout metadata for a node, returning a new store.</summary>
    public VisualMetadataStore ClearLayout(NodeId nodeId) {
        if (!_layouts.ContainsKey(nodeId)) return this;
        return new(_layouts.Remove(nodeId));
    }

    /// <summary>Removes visual layout metadata for all nodes, returning a new store.</summary>
    public VisualMetadataStore Clear() => new();

    /// <summary>Batch updates multiple node layouts, returning a new store.</summary>
    public VisualMetadataStore BatchUpdate(Dictionary<NodeId, VisualLayout> updates) {
        ArgumentNullException.ThrowIfNull(updates);
        var builder = _layouts.ToBuilder();
        foreach (var (nodeId, layout) in updates) {
            builder[nodeId] = layout;
        }
        return new(builder.ToImmutable());
    }

    /// <summary>Exports all visual metadata as a serializable dictionary for persistence.</summary>
    public Dictionary<string, (double X, double Y, double W, double H, int Z, bool Collapsed, string? Color)> ExportMetadata() {
        var result = new Dictionary<string, (double, double, double, double, int, bool, string?)>();
        foreach (var (nodeId, layout) in _layouts) {
            result[nodeId.Value] = (layout.X, layout.Y, layout.Width, layout.Height, layout.ZOrder, layout.IsCollapsed, layout.ColorHex);
        }
        return result;
    }

    /// <summary>Imports visual metadata from a serialized dictionary, returning a new store.</summary>
    public static VisualMetadataStore ImportMetadata(Dictionary<string, (double X, double Y, double W, double H, int Z, bool Collapsed, string? Color)> data) {
        ArgumentNullException.ThrowIfNull(data);
        var builder = ImmutableDictionary.CreateBuilder<NodeId, VisualLayout>();
        foreach (var (nodeIdStr, (x, y, w, h, z, collapsed, color)) in data) {
            var nodeId = NodeId.Parse(nodeIdStr);
            var layout = new VisualLayout { X = x, Y = y, Width = w, Height = h, ZOrder = z, IsCollapsed = collapsed, ColorHex = color };
            builder.Add(nodeId, layout);
        }
        return new(builder.ToImmutable());
    }
}