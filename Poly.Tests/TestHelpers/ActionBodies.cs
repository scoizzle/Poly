using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.TestHelpers;

/// <summary>
/// An action's public method is a scope wrapper (it opens an action scope, calls the private
/// <c>__BodyOf{Action}</c> method and commits or rolls back). The meaning a test asserts about an action's
/// lowered body lives in that body method; these helpers return it, and fall back to the same-named method
/// for a member that is not an action (a generated <c>Create</c>, a handler).
/// A test about the action's PUBLIC shape (signature, access, wrapper identity) reads the same-named method.
/// </summary>
internal static class ActionBodies {
    public const string Prefix = "__BodyOf";

    /// <summary>The method holding the action's lowered body, or the same-named method when there is no wrapper.</summary>
    public static MethodDefinitionNode? BodyMethod(this IEnumerable<MethodDefinitionNode>? methods, string action) =>
        methods?.FirstOrDefault(m => m.Name == Prefix + action)
        ?? methods?.FirstOrDefault(m => m.Name == action);

    public static bool TryGetModuleBody(Domain domain, string entity, string action, out MethodDefinitionNode? method) =>
        RuntimeAnalysisCache.TryGetModuleMethod(domain, entity, Prefix + action, out method)
        || RuntimeAnalysisCache.TryGetModuleMethod(domain, entity, action, out method);
}
