namespace Poly.DomainModeling.Ontology;

/// <summary>
/// One authored entity member: its kind and its name. The pair places the member, not the
/// name alone — a stage and a property may share a name, because stages are not in the entity
/// member scope, so a name-only lookup would place one as the other.
/// </summary>
internal readonly record struct EntityMemberRef(EntityMemberKind Kind, string Name);