using System.Text.Json.Serialization;

namespace Poly.Mcp.Surface;

/// <summary>
/// The single response envelope for the v2 surface. <see cref="Ok"/> means the requested
/// job reached an answer the caller can act on; a rejected model is <c>Ok=false</c> with
/// findings, not an exception. <see cref="Next"/> carries call sketches with arguments so
/// the agent does not have to re-derive them from the schema.
/// </summary>
internal sealed record PolyResponse(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("findings")] IReadOnlyList<PolyFinding> Findings,
    [property: JsonPropertyName("data")] object? Data,
    [property: JsonPropertyName("next")] IReadOnlyList<string> Next,
    [property: JsonPropertyName("state")] PolyState? State,
    [property: JsonPropertyName("errorCode")] string? ErrorCode = null
);

/// <summary>One diagnostic, anchored to the element path the inspect/select grammar uses.</summary>
internal sealed record PolyFinding(
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("hint")] string? Hint = null,
    [property: JsonPropertyName("fix")] string? Fix = null
);

/// <summary>What the caller needs to know about the workspace after every call.</summary>
internal sealed record PolyState(
    [property: JsonPropertyName("workspace")] string Workspace,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("entities")] int Entities,
    [property: JsonPropertyName("instances")] int Instances
);