namespace Poly.DomainModeling;

/// <summary>
/// Represents a behavior or operation that can be performed on an <see cref="Entity"/> (or within a specific <see cref="Stage"/>).
/// 
/// Actions declare parameters, the effects they produce, and any policies that govern their execution.
/// </summary>
public sealed record Action(
    string Name,
    InvocationResult Result,
    IReadOnlyList<Property> Parameters,
    IReadOnlyList<Effect> Effects,
    IReadOnlyList<Policy> Policies
) : DomainMember(Name) {
    public sealed override IEnumerable<Node?> Children => [.. Parameters, Result, .. Policies, .. Effects];
}