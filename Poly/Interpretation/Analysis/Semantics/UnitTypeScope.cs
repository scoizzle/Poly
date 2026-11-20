namespace Poly.Interpretation.Analysis.Semantics;

/// <summary>
/// The bare type names a unit's <c>using</c> directives bring into scope (#998 S2). A name is bound by asking each
/// source what the imported namespaces declare under it: one answer binds, none leaves the name unbound, and several
/// are an ambiguity, never a first-match. It sits beneath every other provider, as a C# namespace import sits beneath
/// a declaration.
/// </summary>
internal sealed class UnitTypeScope(IReadOnlyList<ITypeSource> sources) : IQualifiedNameSource, IAnalysisMetadata {
    private readonly List<string> _namespaces = [];

    /// <summary>Imports <paramref name="namespace"/>; a repeated import is one import.</summary>
    public void Import(string @namespace) {
        if (!_namespaces.Contains(@namespace, StringComparer.Ordinal))
            _namespaces.Add(@namespace);
    }

    /// <summary>Every definition the imported namespaces declare under <paramref name="name"/>, each once.</summary>
    public IReadOnlyList<ITypeDefinition> Candidates(string name) {
        if (name.Contains('.', StringComparison.Ordinal))
            return [];
        var found = new List<ITypeDefinition>();
        foreach (var @namespace in _namespaces) {
            foreach (var source in sources) {
                if (source.TypeIn(@namespace, name) is { } definition && !found.Any(other => TypeDefinitionExtensions.IsSameType(definition, other)))
                    found.Add(definition);
            }
        }
        return found;
    }

    public ITypeDefinition? GetTypeDefinition(string name) =>
        Candidates(name) is [var only] ? only : null;

    public IEnumerable<ITypeDefinition> TypesIn(string? @namespace) => [];

    ITypeDefinition? IQualifiedNameSource.ByQualifiedName(string name) => GetTypeDefinition(name);
}

/// <summary>
/// Opens a <see cref="UnitTypeScope"/> for each <see cref="CompilationUnitNode"/> from its <c>using</c> directives,
/// and reports a bare type name that no declaration binds and several imports do.
/// </summary>
public sealed class UnitTypeScopeAnalyzer : INodeAnalyzer {
    public const string Id = "UnitTypeScope";
    public string PassName => Id;

    public void Analyze(AnalysisContext context, Node node) {
        if (node is CompilationUnitNode { Usings.Count: > 0 } unit) {
            var scope = context.GetMetadata<UnitTypeScope>(default);
            if (scope is null) {
                // A unit's sources are its declarations, then the analysis' target chain (#998 T12a): the CLR is the C#
                // target's source, never one every analysis appends.
                ITypeSource[] sources = context.GetMetadata<AstTypeRegistry>(default) is { } declarations
                    ? [declarations, .. (context.TypeSystem.Target ?? context.TypeSystem).Sources]
                    : [.. (context.TypeSystem.Target ?? context.TypeSystem).Sources];
                scope = new UnitTypeScope(sources);
                context.SetMetadata(default, scope);
                context.AddScopeSource(scope);
            }
            foreach (var @namespace in unit.Usings)
                scope.Import(@namespace);
        }
        else if (BareTypeName(node) is { } name
            && context.GetMetadata<UnitTypeScope>(default) is { } open
            && open.Candidates(name) is { Count: > 1 } candidates
            && context.TypeSystem.Resolve(name) is null) {
            context.ReportDiagnostic(node, DiagnosticSeverity.Error,
                $"'{name}' is ambiguous between {string.Join(" and ", candidates.Select(static type => $"'{type.FullName}'"))}");
        }

        this.AnalyzeChildren(context, node);
    }

    private static string? BareTypeName(Node node) => node switch {
        NamedTypeReference { Namespace: null } named => named.TypeName,
        TypeReference reference and not TypeDefinitionReference => reference.TypeName,
        _ => null
    };
}

public static class UnitTypeScopeAnalyzerExtensions {
    public static AnalyzerBuilder UseUnitTypeScope(this AnalyzerBuilder builder) {
        builder.AddAnalyzer(new UnitTypeScopeAnalyzer());
        return builder;
    }
}