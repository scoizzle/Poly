using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Stores entity type Syntax nodes produced by <see cref="EntitySyntaxPass"/>.
/// Wraps the <see cref="TypeDefinitionNode"/> trees that represent entity types,
/// stage enums, DomainResult infrastructure, and lowered policy methods.
/// </summary>
public sealed record EntitySyntaxMetadata(
    IReadOnlyList<TypeDefinitionNode> Types
) : IAnalysisMetadata;