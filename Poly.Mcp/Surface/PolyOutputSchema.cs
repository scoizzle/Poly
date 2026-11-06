using System.Text.Json.Serialization;

namespace Poly.Mcp.Surface;

/// <summary>
/// The published output schema for the v2 tools: the stable common fields. <c>data</c>,
/// <c>findings</c>, <c>next</c>, and <c>state</c> vary by tool; keeping them out of the
/// schema keeps the fixed tool catalog small while still declaring the machine-readable
/// success/error contract. <c>errorCode</c> has a default so it is optional: the
/// serializer omits nulls, and a required field that is absent fails client validation.
/// </summary>
public sealed record PolyOutputSchema(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("errorCode")] string? ErrorCode = null);