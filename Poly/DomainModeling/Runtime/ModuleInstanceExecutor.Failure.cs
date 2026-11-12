using Poly.DomainModeling.Ontology;
using Poly.Runtime;

using Action = Poly.DomainModeling.Ontology.Action;

namespace Poly.DomainModeling.Runtime;

// Failure partial: threads an action's typed failure into the outcome and projects its guard list.
public sealed partial class ModuleInstanceExecutor {
    private ModuleOutcome MapFailure(string entityName, Action action, DomainFailure failure) =>
        ModuleOutcome.Fail(failure, GuardsFor(entityName, action, failure));

    // The simulate surface's guard list. Only structure is read: the failure kind, its element path and, for the
    // action's own policy block, its guard.
    private IReadOnlyList<string> GuardsFor(string entityName, Action action, DomainFailure failure) {
        if (failure.Kind == DomainFailureKind.Policy
            && string.Equals(failure.ElementPath, $"{entityName}.{action.Name}", StringComparison.Ordinal))
            return failure.Guard is { } guard ? [guard] : [];
        if (failure.Kind == DomainFailureKind.Required && IsNavigationPath(failure.ElementPath))
            return [.. action.Policies.Select(p => p.Name)];
        return [];
    }

    // A path of exactly two segments, Entity.relation, where relation is a navigation of that entity.
    private bool IsNavigationPath(string? path) {
        if (path is null)
            return false;
        var segments = path.Split('.');
        if (segments.Length != 2)
            return false;
        var owner = _domain.Types.OfType<Entity>()
            .FirstOrDefault(e => string.Equals(e.Name, segments[0], StringComparison.Ordinal));
        return owner is not null
            && owner.Navigations.Any(n => string.Equals(n.Name, segments[1], StringComparison.Ordinal));
    }
}