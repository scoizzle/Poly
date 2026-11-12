namespace Poly.DomainModeling.Runtime;

// Invoke partial: argument binding and the action call on a registered instance, including created-child adoption.
public sealed partial class ModuleInstanceExecutor {
    /// <summary>
    /// Invokes an action on <paramref name="target"/>. <paramref name="args"/> are action
    /// parameter values by name; entity-typed parameters take instance ids. Guards,
    /// stage checks and effects are the module body's own. A failure rolls everything back.
    /// </summary>
    public ModuleOutcome Invoke(
        ModuleInstance target, string actionName, IReadOnlyDictionary<string, object?>? args = null) {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrEmpty(actionName);
        lock (_gate) {
            return Transact(() => {
                SyncSubscriptionBodies();
                _born.Clear();
                RequireRegistered(target);
                var action = FindAction(target.EntityName, actionName);
                var method = action is null ? null : FindMethod(target.EntityName, actionName, isStatic: false);
                if (action is not null && method is null)
                    throw new InvalidOperationException(
                        $"Module method '{actionName}' is missing on entity '{target.EntityName}'.");
                if (action is null || method is null)
                    return ModuleOutcome.Fail($"Action '{actionName}' not found on entity '{target.EntityName}'.");

                var parameters = method.Parameters ?? [];
                var supplied = args ?? new Dictionary<string, object?>();
                var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var callArgs = new object?[parameters.Count];
                for (var i = 0; i < parameters.Count; i++) {
                    var parameter = parameters[i];
                    if (IsStoragePort(parameter)) {
                        known.Add(parameter.Name);
                        callArgs[i] = TryValue(supplied, parameter.Name, out var given) && given is IStoragePort port
                            ? port
                            : _port;
                        continue;
                    }
                    known.Add(parameter.Name);
                    if (!TryValue(supplied, parameter.Name, out var raw))
                        throw new HostException($"Missing argument '{parameter.Name}' for action '{actionName}'.");
                    var declared = action.Parameters.FirstOrDefault(p =>
                        string.Equals(p.Name, parameter.Name, StringComparison.OrdinalIgnoreCase));
                    callArgs[i] = CoerceValue(parameter, declared?.Type.TypeName, raw, $"Argument '{parameter.Name}'");
                }
                foreach (var key in supplied.Keys) {
                    if (!known.Contains(key))
                        throw new HostException($"Unknown argument '{key}' for action '{actionName}'.");
                }

                object? ran;
                try {
                    ran = Run(target.EntityName, method, target.Bag, callArgs);
                }
                catch (InvalidOperationException ex) when (
                    ex.Message.Contains("value stack overflow", StringComparison.Ordinal)) {
                    throw new HostException(
                        $"Action invoke depth exceeded while calling '{actionName}'. "
                        + "Possible recursive invoke cycle (e.g. action → invoke self, or OnEntry → invoke → transition loops).");
                }
                if (ran is not DomainResult result)
                    throw new InvalidOperationException(
                        $"Action '{actionName}' on '{target.EntityName}' did not return a DomainResult.");
                if (!result.IsSuccess)
                    return MapFailure(action, result.ErrorMessage);

                var created = AdoptCreatedChildren();
                ModuleInstance? returned = null;
                if (result.Value is not null && !_byBag.TryGetValue(result.Value, out returned)
                    && result.Value is IDictionary<string, object?> returnedBag
                    && action.Result.Members.Count > 0) {
                    // A bare create is returned but linked nowhere: the declared return type
                    // names the entity, and the executor starts tracking the instance.
                    returned = Register(action.Result.Members[0].Type.TypeName, returnedBag);
                    created.Add(returned);
                    created.AddRange(AdoptCreatedChildren());
                }
                return ModuleOutcome.Ok(returned, created);
            }, constraintThrows: true, keepConstraintMutations: true);
        }
    }
}