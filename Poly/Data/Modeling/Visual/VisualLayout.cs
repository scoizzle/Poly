namespace Poly.Data.Modeling.Visual;

/// <summary>
/// Represents the visual position and basic layout properties of a domain object on the authoring canvas.
/// Keyed by NodeId for stable association across edit cycles.
/// Immutable: visual mutations create new instances.
/// </summary>
public sealed record VisualLayout {
    /// <summary>Horizontal position in canvas coordinates (pixels or logical units).</summary>
    public double X { get; init; }

    /// <summary>Vertical position in canvas coordinates (pixels or logical units).</summary>
    public double Y { get; init; }

    /// <summary>Visual width of the node representation (0 = auto-sized).</summary>
    public double Width { get; init; }

    /// <summary>Visual height of the node representation (0 = auto-sized).</summary>
    public double Height { get; init; }

    /// <summary>Optional rotation in degrees (0-360).</summary>
    public double RotationDegrees { get; init; }

    /// <summary>Optional z-order for layering (lower = behind, higher = front).</summary>
    public int ZOrder { get; init; }

    /// <summary>Whether the node is collapsed/minimized in the visual view.</summary>
    public bool IsCollapsed { get; init; }

    /// <summary>Custom visual color (nullable; null = use default/type-based color).</summary>
    public string? ColorHex { get; init; }

    public VisualLayout() { }

    private VisualLayout(double x, double y, double width, double height, double rotation, int zOrder, bool collapsed, string? color) {
        X = x;
        Y = y;
        Width = width;
        Height = height;
        RotationDegrees = rotation;
        ZOrder = zOrder;
        IsCollapsed = collapsed;
        ColorHex = color;
    }

    /// <summary>Creates a new layout with default positioning at origin.</summary>
    public static VisualLayout AtOrigin() => new(0, 0, 0, 0, 0, 0, false, null);

    /// <summary>Creates a new layout at the specified position.</summary>
    public static VisualLayout AtPosition(double x, double y) => new(x, y, 0, 0, 0, 0, false, null);

    /// <summary>Creates a new layout with explicit dimensions.</summary>
    public static VisualLayout WithDimensions(double x, double y, double width, double height) =>
        new(x, y, width, height, 0, 0, false, null);

    /// <summary>Moves this layout to a new position.</summary>
    public VisualLayout MoveTo(double x, double y) => this with { X = x, Y = y };

    /// <summary>Resizes this layout.</summary>
    public VisualLayout Resize(double width, double height) => this with { Width = width, Height = height };

    /// <summary>Rotates this layout.</summary>
    public VisualLayout Rotate(double degrees) => this with { RotationDegrees = degrees % 360 };

    /// <summary>Changes the z-order (layering).</summary>
    public VisualLayout SetZOrder(int zOrder) => this with { ZOrder = zOrder };

    /// <summary>Toggles the collapsed state.</summary>
    public VisualLayout ToggleCollapsed() => this with { IsCollapsed = !IsCollapsed };

    /// <summary>Sets the visual color.</summary>
    public VisualLayout SetColor(string colorHex) => this with { ColorHex = colorHex };
}