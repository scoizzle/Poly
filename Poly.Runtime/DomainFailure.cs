namespace Poly.Runtime;

/// <summary>
/// One typed domain failure. Every member is get-only and set only by the private
/// constructor, so a <c>with</c> expression cannot forge a different kind.
/// <para>
/// <see cref="ElementPath"/> is dotted and outermost-first. <see cref="At"/> prepends a
/// caller's segment and keeps the leaf kind, message and guard: the outermost failure of
/// a nested create still reports the leaf failure, not <see cref="DomainFailureKind.Cascade"/>.
/// </para>
/// <para>
/// <see cref="Render"/> is the legacy-text renderer; it returns <see cref="Message"/>.
/// </para>
/// </summary>
public sealed record DomainFailure {
    public DomainFailureKind Kind { get; }

    public string Message { get; }

    public string? ElementPath { get; }

    /// <summary>The policy or constraint name carried by the Policy, Stage and Constraint kinds.</summary>
    public string? Guard { get; }

    private DomainFailure(DomainFailureKind kind, string message, string? elementPath, string? guard) {
        Kind = kind;
        Message = message;
        ElementPath = elementPath;
        Guard = guard;
    }

    public static DomainFailure Required(string message, string? elementPath = null, string? guard = null) =>
        new(DomainFailureKind.Required, message, elementPath, guard);

    public static DomainFailure Range(string message, string? elementPath = null, string? guard = null) =>
        new(DomainFailureKind.Range, message, elementPath, guard);

    public static DomainFailure Length(string message, string? elementPath = null, string? guard = null) =>
        new(DomainFailureKind.Length, message, elementPath, guard);

    public static DomainFailure Pattern(string message, string? elementPath = null, string? guard = null) =>
        new(DomainFailureKind.Pattern, message, elementPath, guard);

    public static DomainFailure Enum(string message, string? elementPath = null, string? guard = null) =>
        new(DomainFailureKind.Enum, message, elementPath, guard);

    public static DomainFailure Unique(string message, string? elementPath = null, string? guard = null) =>
        new(DomainFailureKind.Unique, message, elementPath, guard);

    public static DomainFailure Stage(string message, string? elementPath = null, string? guard = null) =>
        new(DomainFailureKind.Stage, message, elementPath, guard);

    public static DomainFailure Policy(string message, string? elementPath = null, string? guard = null) =>
        new(DomainFailureKind.Policy, message, elementPath, guard);

    public static DomainFailure Link(string message, string? elementPath = null, string? guard = null) =>
        new(DomainFailureKind.Link, message, elementPath, guard);

    public static DomainFailure Arithmetic(string message, string? elementPath = null, string? guard = null) =>
        new(DomainFailureKind.Arithmetic, message, elementPath, guard);

    public static DomainFailure Argument(string message, string? elementPath = null, string? guard = null) =>
        new(DomainFailureKind.Argument, message, elementPath, guard);

    public static DomainFailure Cascade(string message, string? elementPath = null, string? guard = null) =>
        new(DomainFailureKind.Cascade, message, elementPath, guard);

    public static DomainFailure Conflict(string message, string? elementPath = null, string? guard = null) =>
        new(DomainFailureKind.Conflict, message, elementPath, guard);

    public static DomainFailure Constraint(string message, string? elementPath = null, string? guard = null) =>
        new(DomainFailureKind.Constraint, message, elementPath, guard);

    /// <summary>
    /// Prepends <paramref name="elementPath"/> to <see cref="ElementPath"/> with
    /// <c>.</c> as the separator, keeping kind, message and guard.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="elementPath"/> is null or empty.</exception>
    public DomainFailure At(string elementPath) {
        if (string.IsNullOrEmpty(elementPath))
            throw new ArgumentException("An element path segment is required.", nameof(elementPath));

        var path = string.IsNullOrEmpty(ElementPath)
            ? elementPath
            : $"{elementPath}.{ElementPath}";
        return new DomainFailure(Kind, Message, path, Guard);
    }

    public string Render() => Message;
}