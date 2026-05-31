namespace Poly.DomainModeling.Evolution;

/// <summary>
/// Base type for changes that can be applied through the evolution layer.
/// 
/// In the MVP we support both a small native DomainChange hierarchy and (where needed)
/// an adapter over the legacy DomainMutationIntent types for MCP compatibility.
/// 
/// Changes are pure data. They are interpreted by the applicator inside DomainEvolution
/// to produce a proposed new immutable Domain root, which is then validated by analysis.
/// </summary>
public abstract record DomainChange;