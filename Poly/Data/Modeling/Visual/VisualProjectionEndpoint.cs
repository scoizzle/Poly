using Poly.Data.Modeling.TypeSystem;
using Poly.Syntax;

namespace Poly.Data.Modeling.Visual;

using DomainAction = Action;

/// <summary>
/// Visual projection of a domain object, combining semantic properties with visual layout metadata.
/// Provides a stable interface for visual authoring clients to query and modify domain state.
/// </summary>
public sealed record VisualProjection(
    NodeId NodeId,
    string EntityName,
    string EntityType,
    VisualLayout Layout,
    IReadOnlyCollection<VisualProjection> ChildProjections
) {
    /// <summary>Gets the visual position (X, Y) as a tuple.</summary>
    public (double X, double Y) Position => (Layout.X, Layout.Y);

    /// <summary>Gets the visual dimensions (Width, Height) as a tuple.</summary>
    public (double Width, double Height) Dimensions => (Layout.Width, Layout.Height);

    /// <summary>Returns true if this projection's layout differs from the default layout.</summary>
    public bool HasCustomLayout => Layout != VisualLayout.AtOrigin();
}

/// <summary>
/// Projection endpoints for visual authoring. Provides read/write access to domain state
/// with visual metadata integrated, enabling UI to render and modify the domain model.
/// </summary>
public sealed class VisualProjectionEndpoint {
    private readonly Domain _domain;
    private VisualMetadataStore _visualMetadata;

    public VisualProjectionEndpoint(Domain domain, VisualMetadataStore? visualMetadata = null) {
        ArgumentNullException.ThrowIfNull(domain);
        _domain = domain;
        _visualMetadata = visualMetadata ?? new VisualMetadataStore();
    }

    /// <summary>Gets the current visual metadata store.</summary>
    public VisualMetadataStore VisualMetadata => _visualMetadata;

    /// <summary>Replaces the visual metadata store (for import/restore scenarios).</summary>
    public void RestoreVisualMetadata(VisualMetadataStore metadata) {
        ArgumentNullException.ThrowIfNull(metadata);
        _visualMetadata = metadata;
    }

    /// <summary>Gets the visual projection of the root domain.</summary>
    public VisualProjection ProjectDomain() {
        var children = _domain.Types
            .Select(ProjectType)
            .ToList();

        return new VisualProjection(
            _domain.Id,
            _domain.Name,
            "Domain",
            _visualMetadata.GetLayout(_domain.Id),
            children
        );
    }

    /// <summary>Gets the visual projection of a specific domain type (entity, relationship, etc.).</summary>
    public VisualProjection ProjectType(DomainType type) {
        ArgumentNullException.ThrowIfNull(type);

        var children = type is Entity entity
            ? entity.Properties
                .Cast<DomainObject>()
                .Concat(entity.Stages)
                .Concat(entity.Actions)
                .Select(obj => new VisualProjection(
                    obj.Id,
                    obj is Property p ? p.Name : obj is Stage s ? s.Name : ((DomainAction)obj).Name,
                    obj.GetType().Name,
                    _visualMetadata.GetLayout(obj.Id),
                    []
                ))
                .ToList()
            : new List<VisualProjection>();

        return new VisualProjection(
            type.Id,
            type.Name,
            type.GetType().Name,
            _visualMetadata.GetLayout(type.Id),
            children
        );
    }

    /// <summary>Updates visual layout for a node, returning updated metadata store.</summary>
    public VisualMetadataStore UpdateNodeLayout(NodeId nodeId, VisualLayout newLayout) {
        ArgumentNullException.ThrowIfNull(newLayout);
        _visualMetadata = _visualMetadata.SetLayout(nodeId, newLayout);
        return _visualMetadata;
    }

    /// <summary>Moves a node to a new position.</summary>
    public VisualMetadataStore MoveNode(NodeId nodeId, double x, double y) {
        _visualMetadata = _visualMetadata.MoveNode(nodeId, x, y);
        return _visualMetadata;
    }

    /// <summary>Resizes a node.</summary>
    public VisualMetadataStore ResizeNode(NodeId nodeId, double width, double height) {
        _visualMetadata = _visualMetadata.ResizeNode(nodeId, width, height);
        return _visualMetadata;
    }

    /// <summary>Sets the visual color for a node.</summary>
    public VisualMetadataStore SetNodeColor(NodeId nodeId, string colorHex) {
        _visualMetadata = _visualMetadata.UpdateLayout(nodeId, layout => layout.SetColor(colorHex));
        return _visualMetadata;
    }

    /// <summary>Toggles the collapsed state of a node.</summary>
    public VisualMetadataStore ToggleNodeCollapsed(NodeId nodeId) {
        _visualMetadata = _visualMetadata.UpdateLayout(nodeId, layout => layout.ToggleCollapsed());
        return _visualMetadata;
    }

    /// <summary>Clears visual metadata for a specific node (resets to defaults).</summary>
    public VisualMetadataStore ClearNodeLayout(NodeId nodeId) {
        _visualMetadata = _visualMetadata.ClearLayout(nodeId);
        return _visualMetadata;
    }

    /// <summary>Clears all visual metadata (resets entire canvas).</summary>
    public VisualMetadataStore ClearAllLayouts() {
        _visualMetadata = _visualMetadata.Clear();
        return _visualMetadata;
    }

    /// <summary>Exports visual metadata as a serializable format for persistence.</summary>
    public Dictionary<string, (double X, double Y, double W, double H, int Z, bool Collapsed, string? Color)> ExportVisualMetadata() =>
        _visualMetadata.ExportMetadata();

    /// <summary>Imports visual metadata from a serialized format, restoring visual state.</summary>
    public void ImportVisualMetadata(Dictionary<string, (double X, double Y, double W, double H, int Z, bool Collapsed, string? Color)> data) {
        _visualMetadata = VisualMetadataStore.ImportMetadata(data);
    }
}