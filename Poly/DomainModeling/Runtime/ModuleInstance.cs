namespace Poly.DomainModeling.Runtime;

/// <summary>
/// One module instance the host tracks: the executor's handle, the entity the lowered
/// module defines, and the dictionary that is the instance. Host metadata lives here,
/// beside the bag, never inside it.
/// </summary>
public sealed record ModuleInstance(string Id, string EntityName, IDictionary<string, object?> Bag);

/// <summary>
/// Result of a <see cref="ModuleInstanceExecutor"/> operation. A failed outcome has
/// already been rolled back: bags, links and the storage port are as they were.
/// <see cref="Instance"/> is the created instance (Create) or the entity an action
/// declared with <c>-&gt; Entity</c> returned (Invoke). <see cref="CreatedChildren"/>
/// are the instances the operation added to relationship fields, now registered.
/// </summary>
public sealed record ModuleOutcome(
    bool Succeeded,
    string? Error,
    ModuleInstance? Instance,
    IReadOnlyList<ModuleInstance> CreatedChildren) {
    internal static ModuleOutcome Ok(
        ModuleInstance? instance = null, IReadOnlyList<ModuleInstance>? created = null) =>
        new(true, null, instance, created ?? []);

    internal static ModuleOutcome Fail(string error) => new(false, error, null, []);
}