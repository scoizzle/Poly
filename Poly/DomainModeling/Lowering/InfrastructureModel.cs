namespace Poly.DomainModeling.Lowering;

/// <summary>
/// Derived infrastructure model — sits between the domain model and codegen backends.
///
/// Coordinates five subsystems plus shared effect topology:
/// <list type="bullet">
///   <item><description><see cref="Topology"/> — cross-entity effect coupling
///         (create-in, invoke, subscriptions). Shared fact.</description></item>
///   <item><description><see cref="Aggregate"/> — ownership hierarchy: which entities own
///         which. Shared fact used by storage and transport.</description></item>
///   <item><description><see cref="Behavior"/> — action metadata: parameters, return types,
///         effective policies, stage transitions. Shared fact.</description></item>
///   <item><description><see cref="Storage"/> — columns, navigations, FKs, soft-delete,
///         stage tracking, subscription lists. Storage conventions.</description></item>
///   <item><description><see cref="Transport"/> — resource hierarchy, routing context,
///         exposability. Protocol conventions.</description></item>
/// </list>
/// </summary>
public sealed record InfrastructureModel(
    string DomainName,
    EffectTopology Topology,
    AggregateModel Aggregate,
    BehaviorModel Behavior,
    StorageModel Storage,
    TransportSurface Transport
);