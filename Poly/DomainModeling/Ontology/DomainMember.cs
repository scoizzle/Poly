namespace Poly.DomainModeling;

/// <summary>
/// Base type for all named members within a domain model (entities, stages, actions, policies, properties, etc.).
/// </summary>
public abstract record DomainMember(string Name) : DomainObject;