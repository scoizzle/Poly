using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.DomainModeling.Runtime;

/// <summary>
/// Resolves the short names <c>DomainResult</c> and
/// <c>ConstraintFailureException</c> to the runtime CLR types so
/// <c>Invoke(Member(TypeReference("DomainResult"), "Failure"), …)</c>
/// is a real static call and <c>throw new ConstraintFailureException(msg)</c>
/// is a real CLR throw. Entity type defs stay on
/// <see cref="TypeDefinitionNodeAnalyzer"/>.
/// </summary>
internal sealed class DomainResultTypeProvider(ITypeDefinitionProvider inner) : ITypeDefinitionProvider {
    public ITypeDefinition? GetTypeDefinition(string name) {
        if (string.Equals(name, "DomainResult", StringComparison.Ordinal))
            return ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(DomainResult));
        if (string.Equals(name, "ConstraintFailureException", StringComparison.Ordinal))
            return ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(ConstraintFailureException));
        // Factory Create Ifs name string / Regex; assign lowering uses ClrTypeReference.
        if (string.Equals(name, "string", StringComparison.Ordinal))
            return ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(string));
        if (string.Equals(name, "System.Text.RegularExpressions.Regex", StringComparison.Ordinal))
            return ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(Regex));
        return inner.GetTypeDefinition(name);
    }

    public ITypeDefinition? GetTypeDefinition(Type type) => inner.GetTypeDefinition(type);

    internal static ITypeDefinitionProvider Wrap(ITypeDefinitionProvider inner) =>
        inner is DomainResultTypeProvider wrapped ? wrapped : new DomainResultTypeProvider(inner);
}