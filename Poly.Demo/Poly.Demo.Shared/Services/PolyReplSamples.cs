namespace Poly.Demo.Shared.Services;

/// <summary>A named C#-subset snippet offered by the console page's picker.</summary>
public sealed record PolyReplSample(string Name, string Source);

/// <summary>
/// Console snippets. Each ends in a bare expression so the REPL shows a value;
/// every form here is covered by <c>CSharpScriptTests</c>.
/// </summary>
public static class PolyReplSamples {
    public static readonly IReadOnlyList<PolyReplSample> All = [
        new("Arithmetic", "2 + 3 * 4"),
        new("Locals", "var x = 2 + 3;\nx * 2"),
        new("Loop sum", "var i = 0;\nvar sum = 0;\nwhile (i < 5) { sum = sum + i; i = i + 1; }\nsum"),
        new("Comparison", "1 < 2"),
        new("String", "\"hello\""),
    ];
}
