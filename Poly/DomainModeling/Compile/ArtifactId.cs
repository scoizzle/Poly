namespace Poly.DomainModeling.Compile;

/// <summary>
/// Identity of a compiled artifact: the written name path of the domain element it
/// comes from plus an artifact type, formatted <c>Hotel/Reservation/Confirm#method</c>.
/// It holds only those two strings (no stage, body text or hash), so
/// same-named stage actions share one id.
/// </summary>
public sealed record ArtifactId {
    private const char PathSeparator = '/';
    private const char TypeSeparator = '#';

    private ArtifactId(string path, string type) {
        Path = path;
        Type = type;
    }

    /// <summary>The name path joined with <c>/</c>, for example <c>Hotel/Reservation/Confirm</c>.</summary>
    public string Path { get; }

    /// <summary>The artifact type, for example <c>method</c>. Compared case-sensitively.</summary>
    public string Type { get; }

    /// <summary>The name path split into its names.</summary>
    public IReadOnlyList<string> Segments => Path.Split(PathSeparator);

    /// <summary>
    /// Builds an id from a name path and a type. Throws <see cref="FormatException"/>
    /// when the path is empty or a name or the type is empty, contains whitespace,
    /// <c>/</c> or <c>#</c>. Any other character is accepted.
    /// </summary>
    public static ArtifactId Create(IEnumerable<string> path, string type) {
        ArgumentNullException.ThrowIfNull(path);
        var segments = path.ToArray();
        if (segments.Length == 0)
            throw new FormatException("Artifact id needs at least one name in its path.");
        foreach (var segment in segments)
            RequireValid(segment, "name");
        RequireValid(type, "type");
        return new ArtifactId(string.Join(PathSeparator, segments), type);
    }

    /// <summary>Parses <c>Name/Name#type</c>. Throws <see cref="FormatException"/> when malformed.</summary>
    public static ArtifactId Parse(string text) {
        ArgumentNullException.ThrowIfNull(text);
        var parts = text.Split(TypeSeparator);
        if (parts.Length != 2)
            throw new FormatException($"Artifact id '{text}' must have exactly one '{TypeSeparator}' before its type.");
        return Create(parts[0].Split(PathSeparator), parts[1]);
    }

    public override string ToString() => $"{Path}{TypeSeparator}{Type}";

    internal static void RequireValid(string? part, string what) {
        if (string.IsNullOrEmpty(part)
            || part.Any(c => char.IsWhiteSpace(c) || c == PathSeparator || c == TypeSeparator))
            throw new FormatException(
                $"Artifact id {what} '{part}' must be non-empty with no whitespace, '{PathSeparator}' or '{TypeSeparator}'.");
    }
}
