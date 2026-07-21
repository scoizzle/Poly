namespace Poly.DomainModeling.Lowering;

/// <summary>
/// Derived infrastructure model — sits between the domain model and codegen backends.
///
/// Coordinates five subsystems:
/// <list type="bullet">
///   <item><description><see cref="Storage"/> — columns, navigations, FKs, soft-delete,
///         stage tracking, subscription lists. Storage conventions applied to
///         shared domain facts.</description></item>
///   <item><description><see cref="Aggregate"/> — ownership hierarchy: which entities own
///         which. A derived domain fact shared by storage and transport.</description></item>
///   <item><description><see cref="Behavior"/> — action metadata: parameters, return types,
///         effective policies, stage transitions. A derived domain fact about
///         what can be done with each entity.</description></item>
///   <item><description><see cref="Transport"/> — resource hierarchy, routing context,
///         exposability. Protocol conventions applied to shared domain facts.</description></item>
/// </list>
///
/// Each subsystem is independent and can be consumed or extended independently.
/// </summary>
public sealed record InfrastructureModel(
    string DomainName,
    StorageModel Storage,
    AggregateModel Aggregate,
    BehaviorModel Behavior,
    TransportSurface Transport
);