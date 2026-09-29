using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.DomainModeling.Runtime;

/// <summary>
/// Resolves the short name <c>DomainResult</c> to the runtime
/// <see cref="DomainResult"/> CLR type so
/// <c>Invoke(Member(TypeReference("DomainResult"), "Failure"), …)</c>
/// is a real static call. Entity type defs stay on
/// <see cref="TypeDefinitionNodeAnalyzer"/>.
/// </summary>
internal sealed class DomainResultTypeProvider(ITypeDefinitionProvider inner) : ITypeDefinitionProvider {
    public ITypeDefinition? GetTypeDefinition(string name) {
        if (string.Equals(name, "DomainResult", StringComparison.Ordinal))
            return ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(DomainResult));
        return inner.GetTypeDefinition(name);
    }

    public ITypeDefinition? GetTypeDefinition(Type type) => inner.GetTypeDefinition(type);

    internal static ITypeDefinitionProvider Wrap(ITypeDefinitionProvider inner) =>
        inner is DomainResultTypeProvider wrapped ? wrapped : new DomainResultTypeProvider(inner);
}