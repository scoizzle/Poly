using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.DomainModeling.Runtime;

/// <summary>
/// Resolves the short names <c>DomainResult</c> (and its arity key, erased to
/// <c>DomainResult&lt;object&gt;</c>),
/// <c>ConstraintFailureException</c>, <c>string</c>, and
/// <c>System.Text.RegularExpressions.Regex</c> to the runtime CLR types so
/// <c>Invoke(Member(TypeReference("DomainResult"), "Failure"), …)</c>
/// is a real static call and <c>throw new ConstraintFailureException(msg)</c>
/// is a real CLR throw. Entity type defs stay on
/// <see cref="TypeDefinitionNodeAnalyzer"/>.
/// </summary>
/// <remarks>
/// It is a source the analysis registers as its own (#998 T12b), so it answers these names before the target and a
/// module's own declarations, registered above it, still win. A name it does not hold is the target's.
/// </remarks>
internal sealed class DomainResultTypeProvider : IQualifiedNameSource {
    public static readonly DomainResultTypeProvider Instance = new();

    public ITypeDefinition? ByQualifiedName(string name) {
        if (string.Equals(name, "DomainResult", StringComparison.Ordinal))
            return ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(Poly.Runtime.DomainResult));
        // The generic name erases to DomainResult<object>: the VM is untyped, so one closed
        // CLR type serves every DomainResult<Entity>. The analyzer accepts this hit as-is.
        if (string.Equals(name, AstTypeRegistry.Key("DomainResult", 1), StringComparison.Ordinal))
            return ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(Poly.Runtime.DomainResult<object>));
        if (string.Equals(name, "ConstraintFailureException", StringComparison.Ordinal))
            return ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(ConstraintFailureException));
        // Factory Create Ifs name string / Regex; assign lowering uses a resolved CLR type.
        if (string.Equals(name, "string", StringComparison.Ordinal))
            return ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(string));
        if (string.Equals(name, "System.Text.RegularExpressions.Regex", StringComparison.Ordinal))
            return ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(Regex));
        return null;
    }

    /// <summary>This source holds no declarations of its own; it answers names the runtime family spells.</summary>
    public IEnumerable<ITypeDefinition> TypesIn(string? @namespace) => [];
}