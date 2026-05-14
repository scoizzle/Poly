namespace Poly.DomainModeling.V2;

internal static class DomainMutationEngine {
    public static Domain Apply(Domain domain, DomainMutation mutation)
    {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(mutation);

        var nextDomain = mutation.Kind switch {
            DomainMutationKind.AddEntity => AddEntity(domain, mutation),
            DomainMutationKind.AddProperty => AddProperty(domain, mutation),
            DomainMutationKind.AddStage => AddStage(domain, mutation),
            DomainMutationKind.AddAction => AddAction(domain, mutation),
            DomainMutationKind.AddActionEffect => AddActionEffect(domain, mutation),
            DomainMutationKind.AddRelationship => AddRelationship(domain, mutation),
            _ => throw new NotSupportedException($"Unsupported mutation kind '{mutation.Kind}'.")
        };

        var validation = DomainValidator.Validate(nextDomain);
        if (!validation.IsValid) {
            throw new InvalidOperationException(string.Join(Environment.NewLine, validation.Issues.Select(issue => issue.Message)));
        }

        return nextDomain;
    }

    private static Domain AddEntity(Domain domain, DomainMutation mutation)
    {
        var entityName = Required(mutation.Name, nameof(mutation.Name));
        var entities = domain.Entities.ToList();
        entities.Add(new Entity(entityName, [], [], []));
        return domain with { Entities = entities };
    }

    private static Domain AddProperty(Domain domain, DomainMutation mutation)
    {
        var entityName = Required(mutation.EntityName, nameof(mutation.EntityName));
        var propertyName = Required(mutation.Name, nameof(mutation.Name));
        var type = Required(mutation.Type, nameof(mutation.Type));
        EnsureEntityExists(domain, entityName);

        var entities = domain.Entities
            .Select(entity => entity.Name != entityName
                ? entity
                : entity with {
                    Properties = entity.Properties
                        .Concat([new Property(propertyName, type, mutation.IsRequired, mutation.DefaultValue)])
                        .ToList()
                })
            .ToList();

        return domain with { Entities = entities };
    }

    private static Domain AddStage(Domain domain, DomainMutation mutation)
    {
        var entityName = Required(mutation.EntityName, nameof(mutation.EntityName));
        var stageName = Required(mutation.Name, nameof(mutation.Name));
        var isInitial = mutation.IsRequired;
        EnsureEntityExists(domain, entityName);

        var entities = domain.Entities
            .Select(entity => entity.Name != entityName
                ? entity
                : entity with {
                    Stages = entity.Stages.Concat([new Stage(stageName, isInitial)]).ToList()
                })
            .ToList();

        return domain with { Entities = entities };
    }

    private static Domain AddAction(Domain domain, DomainMutation mutation)
    {
        var entityName = Required(mutation.EntityName, nameof(mutation.EntityName));
        var actionName = Required(mutation.Name, nameof(mutation.Name));
        EnsureEntityExists(domain, entityName);

        var entities = domain.Entities
            .Select(entity => entity.Name != entityName
                ? entity
                : entity with {
                    Actions = entity.Actions.Concat([new Action(actionName, [], [])]).ToList()
                })
            .ToList();

        return domain with { Entities = entities };
    }

    private static Domain AddActionEffect(Domain domain, DomainMutation mutation)
    {
        var entityName = Required(mutation.EntityName, nameof(mutation.EntityName));
        var actionName = Required(mutation.ActionName, nameof(mutation.ActionName));
        var effect = mutation.Effect ?? throw new ArgumentNullException(nameof(mutation.Effect));
        EnsureEntityExists(domain, entityName);
        EnsureActionExists(domain, entityName, actionName);

        var entities = domain.Entities
            .Select(entity => entity.Name != entityName
                ? entity
                : entity with {
                    Actions = entity.Actions.Select(action => action.Name != actionName
                            ? action
                            : action with { Effects = action.Effects.Concat([effect]).ToList() })
                        .ToList()
                })
            .ToList();

        return domain with { Entities = entities };
    }

    private static Domain AddRelationship(Domain domain, DomainMutation mutation)
    {
        var name = Required(mutation.Name, nameof(mutation.Name));
        var source = Required(mutation.EntityName, nameof(mutation.EntityName));
        var target = Required(mutation.TargetEntityName, nameof(mutation.TargetEntityName));
        var kind = mutation.RelationshipKind ?? throw new ArgumentNullException(nameof(mutation.RelationshipKind));

        var relationships = domain.Relationships.ToList();
        relationships.Add(new Relationship(name, source, target, kind));
        return domain with { Relationships = relationships };
    }

    private static string Required(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value)) {
            throw new ArgumentException("Value is required.", paramName);
        }

        return value;
    }

    private static void EnsureEntityExists(Domain domain, string entityName)
    {
        if (domain.Entities.All(entity => !string.Equals(entity.Name, entityName, StringComparison.Ordinal))) {
            throw new InvalidOperationException($"Entity '{entityName}' was not found.");
        }
    }

    private static void EnsureActionExists(Domain domain, string entityName, string actionName)
    {
        var actionExists = domain.Entities
            .Where(entity => string.Equals(entity.Name, entityName, StringComparison.Ordinal))
            .SelectMany(entity => entity.Actions)
            .Any(action => string.Equals(action.Name, actionName, StringComparison.Ordinal));

        if (!actionExists) {
            throw new InvalidOperationException($"Action '{actionName}' was not found on entity '{entityName}'.");
        }
    }
}
