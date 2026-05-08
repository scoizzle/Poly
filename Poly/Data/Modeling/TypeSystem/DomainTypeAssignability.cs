namespace Poly.Data.Modeling.TypeSystem;

internal static class DomainTypeAssignability {
    public static bool CanAssign(DomainType targetType, DomainType sourceType) {
        ArgumentNullException.ThrowIfNull(targetType);
        ArgumentNullException.ThrowIfNull(sourceType);

        if (ReferenceEquals(targetType, sourceType)) {
            return true;
        }

        if (targetType is Entity targetEntity && sourceType is Entity sourceEntity) {
            return IsSameOrDescendant(sourceEntity, targetEntity);
        }

        return false;
    }

    private static bool IsSameOrDescendant(Entity candidate, Entity expectedBase) {
        var visited = new HashSet<Entity>(EqualityComparer<Entity>.Default);
        for (var current = candidate; current is not null; current = current.ParentEntity) {
            if (!visited.Add(current)) {
                break;
            }

            if (ReferenceEquals(current, expectedBase)) {
                return true;
            }
        }

        return false;
    }
}