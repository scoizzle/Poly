using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.TestHelpers;

/// <summary>
/// An action's public method is a scope wrapper (it opens an action scope, calls the private
/// <c>__BodyOf{Action}</c> method and commits or rolls back). The meaning a test asserts about an action's
/// lowered body lives in that body method; these helpers return it and are STRICT: a name that is not an action,
/// or a changed split or prefix, throws instead of silently handing back the wrapper (which would make a body
/// assertion pass vacuously). A test about the action's PUBLIC shape (signature, access, wrapper identity)
/// reads the same-named method itself and does not use these.
/// </summary>
internal static class ActionBodies {
    public const string Prefix = DomainToCSharpExporter.ActionBodyPrefix;

    /// <summary>The method holding the action's lowered body. Throws when there is none.</summary>
    public static MethodDefinitionNode BodyMethod(this IEnumerable<MethodDefinitionNode>? methods, string action) =>
        methods?.FirstOrDefault(m => m.Name == Prefix + action)
        ?? throw new InvalidOperationException(
            $"No '{Prefix}{action}' method: '{action}' is not an action, or the action body split changed.");

    public static bool TryGetModuleBody(Domain domain, string entity, string action, out MethodDefinitionNode? method) =>
        RuntimeAnalysisCache.TryGetModuleMethod(domain, entity, Prefix + action, out method);
}
