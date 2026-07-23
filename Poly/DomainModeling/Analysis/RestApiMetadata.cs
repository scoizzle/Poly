using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Stores REST API surface metadata: routes, DTO shapes, seed hints, query endpoints.
/// Produced by <see cref="RestApiSurfacePass"/>.
/// </summary>
public sealed record RestApiMetadata(
    IReadOnlyList<object> Endpoints,
    IReadOnlyList<object> Dtos,
    IReadOnlyList<object> Seeds
) : IAnalysisMetadata;