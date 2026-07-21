namespace Poly.DomainModeling;

/// <summary>
/// Authoring context for a DSL compilation or MCP session. Holds the active
/// pack set and derived registries (annotation syntax, type mappings, etc.).
///
/// A core-only context (no packs) is the default. Pack-aware parsing is
/// opt-in via <c>new PolyDslParser(text, context)</c>.
///
/// P1: annotation registry only. Type mapping and storage conventions come in P2+.
/// </summary>
public sealed class DomainAuthoringContext {
    /// <summary>The annotation parse/print registry, derived from registered packs.</summary>
    public AnnotationRegistry Annotations { get; } = new();

    /// <summary>Creates a core-only authoring context (no packs enabled).</summary>
    public static DomainAuthoringContext Create() => new();
}