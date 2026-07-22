using Poly.DomainModeling.Lowering;

namespace Poly.DomainModeling;

/// <summary>
/// Authoring context for a DSL compilation or MCP session. Holds the active
/// pack set and derived registries (annotation syntax, type mappings, storage
/// conventions).
///
/// A core-only context (no packs) is the default. Pack-aware parsing is
/// opt-in via <c>new PolyDslParser(text, context)</c>. Infrastructure analysis
/// accepts the same context via <c>InfrastructureAnalyzer.Analyze(authoring)</c>.
/// </summary>
public sealed class DomainAuthoringContext {
    private readonly List<IStorageConvention> _storageConventions = [];

    /// <summary>The annotation parse/print registry, derived from registered packs.</summary>
    public AnnotationRegistry Annotations { get; } = new();

    /// <summary>Pack-overridable domain→SQL/CLR type maps (core defaults underneath).</summary>
    public TypeMappingRegistry TypeMaps { get; } = new();

    /// <summary>Ordered storage convention chain applied after baseline analysis.</summary>
    public IReadOnlyList<IStorageConvention> StorageConventions => _storageConventions;

    /// <summary>Creates a core-only authoring context (no packs enabled).</summary>
    public static DomainAuthoringContext Create() => new();

    /// <summary>Appends a storage convention (later conventions see earlier projections).</summary>
    public DomainAuthoringContext AddStorageConvention(IStorageConvention convention) {
        ArgumentNullException.ThrowIfNull(convention);
        _storageConventions.Add(convention);
        return this;
    }

    /// <summary>
    /// Creates a context pre-configured with the built-in Sql annotation
    /// keywords (<c>column</c>, <c>table</c>). Storage type maps remain
    /// at core generic defaults unless overridden.
    /// </summary>
    public static DomainAuthoringContext CreateWithSqlPack() {
        var ctx = new DomainAuthoringContext();
        ctx.Annotations.Register(new ColumnAnnotationSyntax());
        ctx.Annotations.Register(new TableAnnotationSyntax());
        return ctx;
    }
}