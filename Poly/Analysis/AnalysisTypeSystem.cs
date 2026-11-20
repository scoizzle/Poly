namespace Poly.Analysis;

using Poly.Ast.Nodes;
using Poly.Introspection;

/// <summary>
/// The type system one analysis decides by (#998 T12a, T12b; docs/decisions/2026-11-19-a-type-system-owns-definitions-
/// vocabulary-and-rules.md §2). It is the analysis' layer: its own sources — the declarations it registers and the
/// sources a caller supplies — answer a qualified name first, in their registered precedence order, and its
/// <see cref="Target"/> answers everything else. The CLR is reached only as the Target chain's answer, and nothing falls back beside the
/// layer. Not yet through it (T12c): C#'s declarations still take the CLR's <c>object</c>, <c>int</c> and <c>void</c>
/// directly. Vocabulary and rules are the Target's until T12c gives the layer its declarations.
/// </summary>
internal sealed class AnalysisTypeSystem(ITypeSystem target) : ITypeSystem, ITypeNameLookup {
    /// <summary>This layer's own sources, in query order (index 0 answers first).</summary>
    private readonly List<ITypeSource> _own = [];

    /// <summary>The analysis' own sources, then the target chain's.</summary>
    public IReadOnlyList<ITypeSource> Sources => [.. _own, .. target.Sources];

    public ITypeSystem? Target => target;

    /// <summary>Adds a source that outranks the ones already registered: an analysis' declarations, then a caller's.</summary>
    public void AddSource(ITypeSource source) {
        ArgumentNullException.ThrowIfNull(source);
        _own.Insert(0, source);
    }

    /// <summary>Adds a block of sources that keeps the block's order and outranks the ones already registered.</summary>
    public void AddSources(IEnumerable<ITypeSource> sources) {
        ArgumentNullException.ThrowIfNull(sources);
        _own.InsertRange(0, sources);
    }

    /// <summary>Adds a source beneath every other own source: a unit's <c>using</c> scope sits below a declaration and
    /// above the target. No-op when it is already registered.</summary>
    public void AddScopeSource(ITypeSource source) {
        ArgumentNullException.ThrowIfNull(source);
        if (!_own.Contains(source))
            _own.Add(source);
    }

    /// <summary>
    /// A qualified name through the analysis' own sources, then the Target. A bare name is the unit scope's when it is
    /// registered as an own source, and the Target's otherwise.
    /// </summary>
    public ITypeDefinition? Resolve(string qualifiedName) {
        foreach (var source in _own) {
            if (source is IQualifiedNameSource named && named.ByQualifiedName(qualifiedName) is { } definition)
                return definition;
        }
        return target.Resolve(qualifiedName);
    }

    /// <summary>A declaration's member type reference, resolved through this scope (the layer is the scope it was
    /// declared into).</summary>
    ITypeDefinition? ITypeNameLookup.Lookup(string name) => Resolve(name);

    /// <summary>The definition built for exactly <paramref name="node"/> by one of the own sources, in query order.
    /// Node identity, not name: a domain can hold two definitions with the same name (module and runtime shapes).</summary>
    public ITypeDefinition? DefinitionOf(TypeDefinitionNode node) {
        foreach (var source in _own) {
            if (source is ITypeDefinitionNodeProvider provider && provider.GetTypeDefinition(node) is { } definition)
                return definition;
        }
        return null;
    }

    public ITypeDefinition? Bind(string word) => target.Bind(word);
    public string? Spell(ITypeDefinition definition) => target.Spell(definition);
    public bool IsSame(ITypeDefinition a, ITypeDefinition b) => target.IsSame(a, b);
    public ITypeDefinition? NullableOf(ITypeDefinition definition) => target.NullableOf(definition);
    public ITypeDefinition? ElementType(ITypeDefinition definition) => target.ElementType(definition);

    public bool IsAssignableWithoutConversion(ITypeDefinition destination, ITypeDefinition source) =>
        target.IsAssignableWithoutConversion(destination, source);

    public ITypeConversion? Conversion(ITypeDefinition source, ITypeDefinition destination) =>
        target.Conversion(source, destination);

    public ITypeOperator? Operator(OperatorKind kind, ITypeDefinition left, ITypeDefinition? right) =>
        target.Operator(kind, left, right);

    public TRule? Rule<TRule>() where TRule : class, ITypeSystemRule => target.Rule<TRule>();
}