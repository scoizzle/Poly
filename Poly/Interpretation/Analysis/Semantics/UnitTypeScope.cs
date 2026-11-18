using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Interpretation.Analysis.Semantics;

/// <summary>
/// The bare type names a unit's <c>using</c> directives bring into scope (#998 S2). A name is bound by asking each
/// source what the imported namespaces declare under it: one answer binds, none leaves the name unbound, and several
/// are an ambiguity, never a first-match. It sits beneath every other provider, as a C# namespace import sits beneath
/// a declaration.
/// </summary>
internal sealed class UnitTypeScope(IReadOnlyList<ITypeSource> sources) : ITypeDefinitionProvider, IAnalysisMetadata {
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
                if (source.TypeIn(@namespace, name) is { } definition && !found.Any(definition.IsSameTypeAs))
                    found.Add(definition);
            }
        }
        return found;
    }

    public ITypeDefinition? GetTypeDefinition(string name) =>
        Candidates(name) is [var only] ? only : null;

    public ITypeDefinition? GetTypeDefinition(PrimitiveType primitiveTypeId) => null;

    public IEnumerable<ITypeDefinition> Types => [];
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
                ITypeSource[] sources = context.GetMetadata<AstTypeRegistry>(default) is { } declarations
                    ? [declarations, ClrTypeDefinitionRegistry.Shared]
                    : [ClrTypeDefinitionRegistry.Shared];
                scope = new UnitTypeScope(sources);
                context.SetMetadata(default, scope);
                context.TypeDefinitions.AddFallback(scope);
            }
            foreach (var @namespace in unit.Usings)
                scope.Import(@namespace);
        }
        else if (BareTypeName(node) is { } name
            && context.GetMetadata<UnitTypeScope>(default) is { } open
            && open.Candidates(name) is { Count: > 1 } candidates
            && context.TypeDefinitions.GetTypeDefinition(name) is null) {
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