namespace Poly.Interpretation.Analysis.Semantics;

/// <summary>
/// The bare type names a unit's <c>using</c> directives bring into scope (#998 S2). A name is bound by asking each
/// source what the imported namespaces declare under it: one answer binds, none leaves the name unbound, and several
/// are an ambiguity, never a first-match. It sits beneath every other provider, as a C# namespace import sits beneath
/// a declaration.
/// </summary>
internal sealed class UnitTypeScope(IReadOnlyList<ITypeSource> sources) : IQualifiedNameSource, IAnalysisMetadata {
    private readonly List<string> _namespaces = [];
    private readonly Dictionary<string, IReadOnlyList<ITypeDefinition>> _candidates = new(StringComparer.Ordinal);

    /// <summary>Imports <paramref name="namespace"/>; a repeated import is one import.</summary>
    public void Import(string @namespace) {
        if (!_namespaces.Contains(@namespace, StringComparer.Ordinal)) {
            _namespaces.Add(@namespace);
            _candidates.Clear();
        }
    }

    /// <summary>Every definition the imported namespaces declare under <paramref name="name"/>, each once. Asked once
    /// per name for this scope's imports: a miss probes every source for every namespace.</summary>
    public IReadOnlyList<ITypeDefinition> Candidates(string name) {
        if (name.Contains('.', StringComparison.Ordinal))
            return [];
        if (_candidates.TryGetValue(name, out var cached))
            return cached;
        var found = new List<ITypeDefinition>();
        foreach (var @namespace in _namespaces) {
            foreach (var source in sources) {
                if (source.TypeIn(@namespace, name) is { } definition && !found.Any(other => TypeDefinitionExtensions.IsSameType(definition, other)))
                    found.Add(definition);
            }
        }
        return _candidates[name] = found;
    }

    public ITypeDefinition? GetTypeDefinition(string name) =>
        Candidates(name) is [var only] ? only : null;

    public IEnumerable<ITypeDefinition> TypesIn(string? @namespace) => [];

    ITypeDefinition? IQualifiedNameSource.ByQualifiedName(string name) => GetTypeDefinition(name);
}

/// <summary>
/// Opens the analysis' <see cref="UnitTypeScope"/>: the namespaces its language imports with no directive
/// (<see cref="IImplicitUsings"/>; C#'s implicit usings, #998 S2b), then each <see cref="CompilationUnitNode"/>'s
/// <c>using</c> directives. Reports a bare type name that no declaration binds and several imports do.
/// </summary>
public sealed class UnitTypeScopeAnalyzer : INodeAnalyzer {
    public const string Id = "UnitTypeScope";
    public string PassName => Id;

    public void Analyze(AnalysisContext context, Node node) {
        // The first node this pass visits is the root: a tree built with no unit still has its language's implicit usings.
        if (context.GetMetadata<UnitTypeScope>(default) is null
            && context.TypeSystem.Rule<IImplicitUsings>() is { Namespaces.Count: > 0 } implicitUsings) {
            var opened = Open(context);
            foreach (var @namespace in implicitUsings.Namespaces)
                opened.Import(@namespace);
        }

        if (node is CompilationUnitNode { Usings.Count: > 0 } unit) {
            var scope = context.GetMetadata<UnitTypeScope>(default) ?? Open(context);
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

    private static UnitTypeScope Open(AnalysisContext context) {
        // A scope's sources are the unit's declarations, then the analysis' target chain (#998 T12a): the CLR is the C#
        // target's source, never one every analysis appends.
        ITypeSource[] sources = context.GetMetadata<AstTypeRegistry>(default) is { } declarations
            ? [declarations, .. (context.TypeSystem.Target ?? context.TypeSystem).Sources]
            : [.. (context.TypeSystem.Target ?? context.TypeSystem).Sources];
        var scope = new UnitTypeScope(sources);
        context.SetMetadata(default, scope);
        context.AddScopeSource(scope);
        return scope;
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