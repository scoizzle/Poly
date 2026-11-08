namespace Poly.Compile;

/// <summary>
/// One finding, used unchanged by batch text, JSON and LSP. Lines and columns are 1-based and
/// the end is exclusive. <see cref="Placed"/> is false when the finding could not be tied to a
/// source span; it is then reported at 1:1.
/// </summary>
public sealed record CompileDiagnostic(
    string File,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    string Severity,
    string Code,
    string Message,
    string Path,
    bool Placed) {
    public bool IsError => Severity == Severities.Error;
    public bool IsWarning => Severity == Severities.Warning;
}

/// <summary>Severity names as they appear in JSON and text output.</summary>
public static class Severities {
    public const string Error = "error";
    public const string Warning = "warning";
    public const string Information = "information";
    public const string Hint = "hint";

    /// <summary>Rank for <c>--verbosity</c>: 0 error, 1 warning, 2 information, 3 hint.</summary>
    public static int Rank(string severity) => severity switch {
        Error => 0,
        Warning => 1,
        Information => 2,
        _ => 3,
    };
}

/// <summary>Compile-stage codes. Analyzer codes (DMSTR001, …) pass through unchanged.</summary>
public static class DiagnosticCodes {
    public const string Parse = "DSL0001";
    public const string UnknownExtension = "DSL0002";
    public const string DuplicateExtension = "DSL0003";
    public const string Refused = "DSL0004";
    public const string AnalysisUncoded = "DM0000";
}