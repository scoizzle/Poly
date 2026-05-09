using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

public static class DomainQueryExtensions {
    public static IEnumerable<DomainType> GetAvailableTypes(this Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);

        return domain.Types;
    }

    public static IEnumerable<Entity> GetAvailableEntities(this Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);

        return domain.Types.OfType<Entity>();
    }

    public static IEnumerable<Primitive> GetAvailablePrimitives(this Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);

        return domain.Types.OfType<Primitive>();
    }

    public static IEnumerable<Event> GetAvailableEventTypes(this Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);

        return domain.Types.OfType<Event>();
    }

    public static IEnumerable<Relationship> GetAvailableRelationships(this Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);

        return domain.Relationships;
    }

    public static IEnumerable<ImportedContract> GetAvailableImportedContracts(this Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);
        return domain.Objects.OfType<ImportedContract>();
    }

    public static IEnumerable<ContractBinding> GetAvailableContractBindings(this Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);
        return domain.Objects.OfType<ContractBinding>();
    }

    public static Entity? FindEntity(this Domain domain, string name) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(name);

        return domain.Types.OfType<Entity>().FirstOrDefault(entity => string.Equals(entity.Name, name, StringComparison.Ordinal));
    }

    public static Entity RequireEntity(this Domain domain, string name) {
        return domain.FindEntity(name) ?? throw new InvalidOperationException($"Entity '{name}' was not found in domain '{domain.Name}'.");
    }

    public static Actor? FindActor(this Domain domain, string name) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(name);

        return domain.Types.OfType<Actor>().FirstOrDefault(actor => string.Equals(actor.Name, name, StringComparison.Ordinal));
    }

    public static Actor RequireActor(this Domain domain, string name) {
        return domain.FindActor(name) ?? throw new InvalidOperationException($"Actor '{name}' was not found in domain '{domain.Name}'.");
    }

    public static Primitive? FindPrimitive(this Domain domain, string name) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(name);

        return domain.Types.OfType<Primitive>().FirstOrDefault(primitive => string.Equals(primitive.Name, name, StringComparison.Ordinal));
    }

    public static Primitive RequirePrimitive(this Domain domain, string name) {
        return domain.FindPrimitive(name) ?? throw new InvalidOperationException($"Primitive '{name}' was not found in domain '{domain.Name}'.");
    }

    public static DomainType? FindType(this Domain domain, string name) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(name);

        return domain.Types.FirstOrDefault(type => string.Equals(type.Name, name, StringComparison.Ordinal));
    }

    public static DomainType RequireType(this Domain domain, string name) {
        return domain.FindType(name) ?? throw new InvalidOperationException($"Type '{name}' was not found in domain '{domain.Name}'. Use GetDomain to see available types.");
    }

    public static Event? FindEventType(this Domain domain, string name) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(name);

        return domain.Types.OfType<Event>().FirstOrDefault(@event => string.Equals(@event.Name, name, StringComparison.Ordinal));
    }

    public static Event RequireEventType(this Domain domain, string name) {
        return domain.FindEventType(name) ?? throw new InvalidOperationException($"Event '{name}' was not found in domain '{domain.Name}'.");
    }

    public static Relationship? FindRelationship(this Domain domain, string name) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(name);

        return domain.Relationships.FirstOrDefault(relationship => string.Equals(relationship.Name, name, StringComparison.Ordinal));
    }

    public static Relationship RequireRelationship(this Domain domain, string name) {
        return domain.FindRelationship(name) ?? throw new InvalidOperationException($"Relationship '{name}' was not found in domain '{domain.Name}'.");
    }

    public static ImportedContract? FindImportedContract(this Domain domain, string name) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(name);
        return domain.GetAvailableImportedContracts().FirstOrDefault(contract => string.Equals(contract.Name, name, StringComparison.Ordinal));
    }

    public static ImportedContract RequireImportedContract(this Domain domain, string name) {
        return domain.FindImportedContract(name) ?? throw new InvalidOperationException($"Imported contract '{name}' was not found in domain '{domain.Name}'.");
    }

    public static ContractBinding? FindContractBinding(this Domain domain, string name) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(name);
        return domain.GetAvailableContractBindings().FirstOrDefault(binding => string.Equals(binding.Name, name, StringComparison.Ordinal));
    }

    public static ContractBinding RequireContractBinding(this Domain domain, string name) {
        return domain.FindContractBinding(name) ?? throw new InvalidOperationException($"Contract binding '{name}' was not found in domain '{domain.Name}'.");
    }

    public static IEnumerable<Relationship> FindRelationshipsBySource(this Domain domain, DomainType source) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(source);

        return domain.Relationships.Where(relationship => ReferenceEquals(relationship.Source, source));
    }

    public static IEnumerable<Relationship> FindRelationshipsByTarget(this Domain domain, DomainType target) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(target);

        return domain.Relationships.Where(relationship => ReferenceEquals(relationship.Target, target));
    }

    public static IEnumerable<Relationship> FindOwnedRelationships(this Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);

        return domain.Relationships.Where(relationship => relationship.SourceOwnsTarget);
    }

    public static Stage? FindStage(this Entity entity, string name) {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(name);

        return entity.Stages.FirstOrDefault(stage => string.Equals(stage.Name, name, StringComparison.Ordinal));
    }

    public static Stage RequireStage(this Entity entity, string name) {
        return entity.FindStage(name) ?? throw new InvalidOperationException($"Stage '{name}' was not found on entity '{entity.Name}'.");
    }

    public static Property? FindProperty(this Entity entity, string name) {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(name);

        return entity.Properties.FirstOrDefault(property => string.Equals(property.Name, name, StringComparison.Ordinal));
    }

    public static Property RequireProperty(this Entity entity, string name) {
        return entity.FindProperty(name) ?? throw new InvalidOperationException($"Property '{name}' was not found on entity '{entity.Name}'.");
    }

    public static Action? FindAction(this Entity entity, string name) {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(name);

        return entity.Actions.FirstOrDefault(action => string.Equals(action.Name, name, StringComparison.Ordinal));
    }

    public static Action RequireAction(this Entity entity, string name) {
        return entity.FindAction(name) ?? throw new InvalidOperationException($"Action '{name}' was not found on entity '{entity.Name}'.");
    }

    public static Action? FindActionInHierarchy(this Entity entity, string name) {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(name);

        var visited = new HashSet<NodeId>();
        for (var current = entity; current is not null; current = current.ParentEntity) {
            if (!visited.Add(current.Id)) {
                break;
            }

            var local = current.FindAction(name);
            if (local is not null) {
                return local;
            }
        }

        return null;
    }

    public static IEnumerable<Action> GetAvailableActionsInHierarchy(this Entity entity) {
        ArgumentNullException.ThrowIfNull(entity);

        var actionsByName = new Dictionary<string, Action>(StringComparer.Ordinal);
        var visited = new HashSet<NodeId>();

        for (var current = entity; current is not null; current = current.ParentEntity) {
            if (!visited.Add(current.Id)) {
                break;
            }

            foreach (var action in current.Actions) {
                _ = actionsByName.TryAdd(action.Name, action);
            }
        }

        return actionsByName.Values;
    }

    public static Event? FindEvent(this Entity entity, string name) {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(name);

        return entity.Events.FirstOrDefault(@event => string.Equals(@event.Name, name, StringComparison.Ordinal));
    }

    public static Event RequireEvent(this Entity entity, string name) {
        return entity.FindEvent(name) ?? throw new InvalidOperationException($"Event '{name}' was not found on entity '{entity.Name}'.");
    }

    public static Property? FindProperty(this Event @event, string name) {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(name);

        return @event.Properties.FirstOrDefault(property => string.Equals(property.Name, name, StringComparison.Ordinal));
    }

    public static Property RequireProperty(this Event @event, string name) {
        return @event.FindProperty(name)
               ?? throw new InvalidOperationException($"Property '{name}' was not found on event '{@event.Name}'.");
    }

    public static Action? FindAction(this Stage stage, string name) {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(name);

        return stage.Actions.FirstOrDefault(action => string.Equals(action.Name, name, StringComparison.Ordinal));
    }

    public static Action RequireAction(this Stage stage, string name) {
        return stage.FindAction(name) ?? throw new InvalidOperationException($"Action '{name}' was not found on stage '{stage.Name}'.");
    }

    public static Action? FindActionInHierarchy(this Stage stage, string name) {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(name);

        var visited = new HashSet<NodeId>();
        for (var current = stage; current is not null; current = current.Parent) {
            if (!visited.Add(current.Id)) {
                break;
            }

            var local = current.FindAction(name);
            if (local is not null) {
                return local;
            }
        }

        return null;
    }

    public static IEnumerable<Action> GetAvailableActionsInHierarchy(this Stage stage) {
        ArgumentNullException.ThrowIfNull(stage);

        var actionsByName = new Dictionary<string, Action>(StringComparer.Ordinal);
        var visited = new HashSet<NodeId>();
        for (var current = stage; current is not null; current = current.Parent) {
            if (!visited.Add(current.Id)) {
                break;
            }

            foreach (var action in current.Actions) {
                _ = actionsByName.TryAdd(action.Name, action);
            }
        }
        return actionsByName.Values;
    }

    public static IEnumerable<Policy> GetAvailablePoliciesInHierarchy(this Stage stage) {
        ArgumentNullException.ThrowIfNull(stage);

        var policiesByName = new Dictionary<string, Policy>(StringComparer.Ordinal);
        var visited = new HashSet<NodeId>();
        for (var current = stage; current is not null; current = current.Parent) {
            if (!visited.Add(current.Id)) {
                break;
            }

            foreach (var policy in current.Policies) {
                _ = policiesByName.TryAdd(policy.Name, policy);
            }
        }
        return policiesByName.Values;
    }

    public static T? FindEffect<T>(this Action action) where T : Effect {
        ArgumentNullException.ThrowIfNull(action);

        return action.Effects.OfType<T>().FirstOrDefault();
    }

    public static T RequireEffect<T>(this Action action) where T : Effect {
        return action.FindEffect<T>()
               ?? throw new InvalidOperationException($"Effect '{typeof(T).Name}' was not found on action '{action.Name}'.");
    }

    public static Property? FindParameter(this Action action, string name) {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(name);

        return action.Parameters.OfType<Property>()
            .FirstOrDefault(parameter => string.Equals(parameter.Name, name, StringComparison.Ordinal));
    }

    public static Property RequireParameter(this Action action, string name) {
        return action.FindParameter(name)
               ?? throw new InvalidOperationException($"Parameter '{name}' was not found on action '{action.Name}'.");
    }

    public static IEnumerable<Event> GetAvailablePublishedEvents(this Action action) {
        ArgumentNullException.ThrowIfNull(action);

        return action.Effects.OfType<PublishEvent>().Select(effect => effect.Event);
    }

    public static IEnumerable<Stage> GetAvailableTransitionTargets(this Action action) {
        ArgumentNullException.ThrowIfNull(action);

        return action.Effects.OfType<StageTransition>().Select(effect => effect.TargetStage);
    }

    // ── Policy helpers ───────────────────────────────────────────────────────

    public static Policy? FindPolicy(this Entity entity, string name) {
        ArgumentNullException.ThrowIfNull(entity);
        return entity.Policies.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
    }

    public static Policy RequirePolicy(this Entity entity, string name) =>
        entity.FindPolicy(name) ?? throw new InvalidOperationException($"Policy '{name}' was not found on entity '{entity.Name}'.");

    public static Policy? FindPolicy(this Stage stage, string name) {
        ArgumentNullException.ThrowIfNull(stage);
        return stage.Policies.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
    }

    public static Policy RequirePolicy(this Stage stage, string name) =>
        stage.FindPolicy(name) ?? throw new InvalidOperationException($"Policy '{name}' was not found on stage '{stage.Name}'.");

    public static Policy? FindPolicy(this Property property, string name) {
        ArgumentNullException.ThrowIfNull(property);
        return property.Policies.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
    }

    public static Policy RequirePolicy(this Property property, string name) =>
        property.FindPolicy(name) ?? throw new InvalidOperationException($"Policy '{name}' was not found on property '{property.Name}'.");

    public static Policy? FindPolicy(this Action action, string name) {
        ArgumentNullException.ThrowIfNull(action);
        return action.Policies.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
    }

    public static Policy RequirePolicy(this Action action, string name) =>
        action.FindPolicy(name) ?? throw new InvalidOperationException($"Policy '{name}' was not found on action '{action.Name}'.");

    // ── Rule helpers ─────────────────────────────────────────────────────────

    public static Rule? FindRule(this Policy policy, string name) {
        ArgumentNullException.ThrowIfNull(policy);
        return policy.Rules.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.Ordinal));
    }

    public static Rule RequireRule(this Policy policy, string name) =>
        policy.FindRule(name) ?? throw new InvalidOperationException($"Rule '{name}' was not found in policy '{policy.Name}'.");

}