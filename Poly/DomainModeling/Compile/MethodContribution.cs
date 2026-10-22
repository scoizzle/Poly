using Poly.Ast.Nodes;

namespace Poly.DomainModeling.Compile;

/// <summary>
/// A parameter a library adds to a produced method. <see cref="ThreadToCallers"/>
/// means core inserts copies into every method that calls this one, in order by
/// library id then name, before the method's first optional parameter. The
/// library does not walk callers.
/// </summary>
public sealed record ContributedParameter(string Name, Node Type, bool ThreadToCallers);

/// <summary>
/// Where injected statements land inside the anchor method.
/// <see cref="Start"/> is the method entry; <see cref="BeforeConstruction"/>
/// is immediately before the first <c>new</c>; <see cref="BeforeReturn"/> is
/// immediately before the method's final return.
/// </summary>
public enum ContributionPlacement { Start, BeforeConstruction, BeforeReturn }

/// <summary>
/// Splice built from one <see cref="ConstraintContribution"/>. Not a library
/// door. <see cref="NewMethod"/> is added when the type does not already define
/// <see cref="MethodName"/>. <see cref="Statements"/> are placed per
/// <see cref="Placement"/>. Parameters are inserted before the method's first
/// optional parameter.
/// </summary>
public sealed record MethodContribution(
    string LibraryId,
    string TargetTypeName,
    string MethodName,
    ContributionPlacement Placement,
    IReadOnlyList<ContributedParameter> Parameters,
    MethodDefinitionNode? NewMethod = null,
    IReadOnlyList<Node>? Statements = null,
    string? Provenance = null);