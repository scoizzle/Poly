using Poly.Runtime;

namespace Poly.DomainModeling.Runtime;

// Create partial: the static Create factory call that supplies a dictionary This and registers the instance.
public sealed partial class ModuleInstanceExecutor {
    /// <summary>
    /// Runs the entity's cached static <c>Create</c> on a dictionary <c>This</c>.
    /// <paramref name="values"/> are property values by name (case-insensitive) from the
    /// tool layer: JSON-ish scalars, instance ids for entity-typed values.
    /// </summary>
    public ModuleOutcome Create(string entityName, IReadOnlyDictionary<string, object?> values) {
        ArgumentException.ThrowIfNullOrEmpty(entityName);
        ArgumentNullException.ThrowIfNull(values);
        lock (_gate) {
            return Transact(() => {
                SyncSubscriptionBodies();
                _born.Clear();
                var entity = RequireEntity(entityName);
                var create = FindMethod(entityName, "Create", isStatic: true)
                    ?? throw new HostException($"Entity '{entityName}' has no Create factory.");
                var parameters = create.Parameters ?? [];
                var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var args = new object?[parameters.Count];
                for (var i = 0; i < parameters.Count; i++) {
                    var parameter = parameters[i];
                    if (IsStoragePort(parameter)) {
                        args[i] = _port;
                        continue;
                    }
                    known.Add(parameter.Name);
                    var property = entity.Properties.FirstOrDefault(p =>
                        string.Equals(p.Name, parameter.Name, StringComparison.OrdinalIgnoreCase));
                    var supplied = TryValue(values, parameter.Name, out var raw);
                    var label = $"Property '{property?.Name ?? parameter.Name}'";
                    args[i] = supplied
                        ? CoerceValue(parameter, property?.Type.TypeName, raw, label, namedTypeFailure: true)
                        : CoerceValue(parameter, null, DefaultOf(parameter), label);
                }
                foreach (var key in values.Keys) {
                    if (!known.Contains(key))
                        throw new HostException($"Unknown property '{key}' for entity '{entityName}'.");
                }

                if (Run(entityName, create, self: null, args) is not IDomainResult result)
                    throw new InvalidOperationException($"Create on '{entityName}' did not return a DomainResult.");
                if (!result.IsSuccess)
                    return ModuleOutcome.Fail(result.ErrorMessage ?? "Create failed.");
                if (result is not DomainResult<object> { Value: IDictionary<string, object?> bag })
                    throw new InvalidOperationException($"Create on '{entityName}' did not return a dictionary instance.");

                var instance = Register(entityName, bag);
                return ModuleOutcome.Ok(instance, AdoptCreatedChildren());
            }, constraintThrows: true);
        }
    }
}