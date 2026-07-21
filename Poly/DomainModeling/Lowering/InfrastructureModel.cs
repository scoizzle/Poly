namespace Poly.DomainModeling.Lowering;

/// <summary>
/// Derived infrastructure model — sits between the domain model and codegen backends.
///
/// Coordinates two independent subsystems:
/// <list type="bullet">
///   <item><description><see cref="Storage"/> — aggregate boundaries, keys, columns,
///         navigations, foreign keys, soft-delete, stage tracking, subscription lists.
///         Everything about how domain data gets persisted and fetched.</description></item>
///   <item><description><see cref="Transport"/> — action surface (parameters, return types,
///         policies, stage transitions), cross-entity effect topology (create-in, invoke,
///         subscriptions). Everything about how domain data is served over protocols.</description></item>
/// </list>
///
/// Protocol-specific and storage-specific codegen backends consume their respective subsystem
/// and map decisions to their own conventions without re-deriving from raw domain types.
/// </summary>
public sealed record InfrastructureModel(
    string DomainName,
    StorageModel Storage,
    TransportModel Transport
);