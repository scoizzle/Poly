using Poly.Ast.Nodes;

namespace Poly.DomainModeling.Compile;

/// <summary>
/// A parameter a library adds to a produced method. <see cref="ThreadToCallers"/>
/// means core appending copies it onto every method that calls this one, as a
/// suffix ordered by library id then name. The library does not walk callers.
/// </summary>
public sealed record ContributedParameter(string Name, Node Type, bool ThreadToCallers);

/// <summary>
/// Splice built from one <see cref="CompiledConstraint"/>. Not a library door.
/// <see cref="NewMethod"/> is added when the type does not already define
/// <see cref="MethodName"/>. <see cref="Statements"/> go at the start of the
/// body, or immediately before the first construction when
/// <see cref="BeforeConstruction"/> is set. Parameters are appended after the
/// method's own parameters.
/// </summary>
public sealed record MethodContribution(
    string LibraryId,
    string TargetTypeName,
    string MethodName,
    IReadOnlyList<ContributedParameter> Parameters,
    MethodDefinitionNode? NewMethod = null,
    IReadOnlyList<Node>? Statements = null,
    bool BeforeConstruction = false);