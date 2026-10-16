using Poly.Ast.Nodes;

namespace Poly.DomainModeling.Compile;

/// <summary>
/// A parameter a library adds to a produced method. <see cref="ThreadToCallers"/>
/// means core appending copies it onto every method that calls this one, as a
/// suffix ordered by library id then name. The library does not walk callers.
/// </summary>
public sealed record ContributedParameter(string Name, Node Type, bool ThreadToCallers);

/// <summary>
/// A stub, a statement prefix, or both, contributed by one library onto one
/// produced method. <see cref="NewMethod"/> is added when the type does not
/// already define <see cref="MethodName"/>. <see cref="PrependStatements"/> are
/// inserted at the start of an existing (or just added) body. Parameters are
/// appended after the method's own parameters.
/// </summary>
public sealed record MethodContribution(
    string LibraryId,
    string TargetTypeName,
    string MethodName,
    IReadOnlyList<ContributedParameter> Parameters,
    MethodDefinitionNode? NewMethod = null,
    IReadOnlyList<Node>? PrependStatements = null);