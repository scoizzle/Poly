namespace Poly.DomainModeling.Runtime;

/// <summary>
/// One module instance the host tracks: the executor's handle, the entity the lowered
/// module defines, and the dictionary that is the instance. Host metadata lives here,
/// beside the bag, never inside it.
/// </summary>
public sealed record ModuleInstance(string Id, string EntityName, IDictionary<string, object?> Bag);

/// <summary>
/// What a <see cref="ModuleInstanceExecutor.Link"/> or
/// <see cref="ModuleInstanceExecutor.Unlink"/> call actually changed. A repeat of a link
/// or unlink that already held is a succeed-with-no-change,
/// <see cref="AlreadyLinked"/> or <see cref="NotLinked"/>; the outcome stays successful.
/// </summary>
public enum LinkChange {
    /// <summary>The link was absent and is now present.</summary>
    Linked,
    /// <summary>The link was already present; nothing changed.</summary>
    AlreadyLinked,
    /// <summary>The link was present and is now absent.</summary>
    Unlinked,
    /// <summary>The link was already absent; nothing changed.</summary>
    NotLinked
}

/// <summary>
/// Result of a <see cref="ModuleInstanceExecutor"/> operation. A failed outcome has
/// already been rolled back: bags, links and the storage port are as they were.
/// <see cref="Instance"/> is the created instance (Create) or the entity an action
/// declared with <c>-&gt; Entity</c> returned (Invoke). <see cref="CreatedChildren"/>
/// are the instances the operation added to relationship fields, now registered.
/// <see cref="Change"/> is the link mutation a linked operation performed, or
/// <c>null</c> when the operation was not a Link/Unlink.
/// </summary>
public sealed record ModuleOutcome(
    bool Succeeded,
    string? Error,
    ModuleInstance? Instance,
    IReadOnlyList<ModuleInstance> CreatedChildren,
    IReadOnlyList<string> FailedGuards = null!,
    LinkChange? Change = null) {
    internal static ModuleOutcome Ok(
        ModuleInstance? instance = null, IReadOnlyList<ModuleInstance>? created = null,
        LinkChange? change = null) =>
        new(true, null, instance, created ?? [], [], change);

    internal static ModuleOutcome Fail(string error, IReadOnlyList<string>? guards = null) =>
        new(false, error, null, [], guards ?? []);
}