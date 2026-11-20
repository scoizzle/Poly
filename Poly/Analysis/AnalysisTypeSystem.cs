namespace Poly.Analysis;

using Poly.Ast.Nodes;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;

/// <summary>
/// The type system one analysis decides by (#998 T12a–T12c; docs/decisions/2026-11-19-a-type-system-owns-definitions-
/// vocabulary-and-rules.md §2). It is the analysis' layer: its own sources — the declarations it registers and the
/// sources a caller supplies — answer a qualified name first, in their registered precedence order, and its
/// <see cref="Target"/> answers everything else. The CLR is reached only as the Target chain's answer, and nothing falls
/// back beside the layer. The declarations it holds belong to it (T12c): a declared type, and any synthetic collection or
/// generic parameter built over one, reports this layer as its <see cref="ITypeDefinition.System"/>, so two analyses'
/// declarations are never the same type while a CLR collection stays C#'s
/// (<see cref="AstTypeIdentity"/> is the body both share). Vocabulary and rules are the Target's unless a type system
/// declares its own.
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

    /// <summary>
    /// Identity in the layer (#998 T12c): the definitions this layer owns answer through the body C# shares
    /// (<see cref="AstTypeIdentity"/>: reference identity for a declaration, kind and element for a synthetic collection,
    /// name and declaring type for a generic parameter); everything else is the Target's, and a definition this layer
    /// owns is never the same as one another system owns.
    /// </summary>
    public bool IsSame(ITypeDefinition a, ITypeDefinition b) {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (ReferenceEquals(a, b))
            return true;
        var left = ReferenceEquals(a.System, this);
        var right = ReferenceEquals(b.System, this);
        if (left || right)
            return left && right && AstTypeIdentity.IsSame(a, b);
        return target.IsSame(a, b);
    }

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