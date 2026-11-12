namespace Poly.DomainModeling.Ontology;

/// <summary>
/// The kind of one authored entity member recorded in <see cref="Entity.MemberOrder"/>.
/// Exactly the member lists an entity owns: properties, navigations, stages, entity-level
/// actions and entity-level policies. An entity-level subscription and a stage-scoped action
/// or policy are not entity members — each already lives in an ordered list on its own node.
/// Members are ordered as declared: <see cref="Property"/>, <see cref="Navigation"/>,
/// <see cref="Stage"/>, <see cref="Action"/>, <see cref="Policy"/>.
/// </summary>
internal enum EntityMemberKind {
    Property,
    Navigation,
    Stage,
    Action,
    Policy,
}