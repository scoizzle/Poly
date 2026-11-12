using System.Text.Json;

namespace Poly.Mcp.Surface;

/// <summary>Optional bounds on one argument's raw JSON value. A null bound is not enforced.</summary>
internal readonly record struct ArgumentLimit(int? MaxChars, int? MaxItems);

/// <summary>
/// The one registry of argument limits. It is empty today: the slice that owns a number adds
/// its entry here. The check takes the map as a parameter, so a synthetic map proves enforcement
/// without a mutable static, and the shipped registry is visible in a diff when it changes.
/// </summary>
internal static class ArgumentLimits {
    public static IReadOnlyDictionary<(string Tool, string Argument), ArgumentLimit> Registry { get; } =
        new Dictionary<(string Tool, string Argument), ArgumentLimit>();

    /// <summary>
    /// Null when the raw value is inside its limit; otherwise the caller-facing reason. Character
    /// counts measure the string for a JSON string and the raw text for any other value; item
    /// counts measure a JSON array. A limit with both bounds null limits nothing.
    /// </summary>
    public static string? Check(
        string tool,
        string argument,
        JsonElement value,
        IReadOnlyDictionary<(string Tool, string Argument), ArgumentLimit> limits) {
        if (!limits.TryGetValue((tool, argument), out var limit))
            return null;

        if (limit.MaxChars is { } maxChars) {
            var length = value.ValueKind == JsonValueKind.String
                ? value.GetString()!.Length
                : value.GetRawText().Length;
            if (length > maxChars)
                return $"Argument '{argument}' for {tool} is {length} characters; the limit is {maxChars}.";
        }

        if (limit.MaxItems is { } maxItems && value.ValueKind == JsonValueKind.Array) {
            var count = value.GetArrayLength();
            if (count > maxItems)
                return $"Argument '{argument}' for {tool} has {count} items; the limit is {maxItems}.";
        }

        return null;
    }
}