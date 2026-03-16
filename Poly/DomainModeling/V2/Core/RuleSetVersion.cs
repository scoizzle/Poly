namespace Poly.DomainModeling.V2.Core;

public readonly record struct RuleSetVersion : IComparable<RuleSetVersion> {
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }

    public RuleSetVersion(int major, int minor, int patch)
    {
        if (major < 0 || minor < 0 || patch < 0) {
            throw new ArgumentOutOfRangeException(nameof(major), "Version parts must be non-negative.");
        }

        Major = major;
        Minor = minor;
        Patch = patch;
    }

    public RuleSetVersion(string value)
        : this(Parse(value))
    {
    }

    private RuleSetVersion((int Major, int Minor, int Patch) parts)
        : this(parts.Major, parts.Minor, parts.Patch)
    {
    }

    public static RuleSetVersion ParseVersion(string value) => new(value);

    public int CompareTo(RuleSetVersion other)
    {
        var majorComparison = Major.CompareTo(other.Major);
        if (majorComparison != 0) {
            return majorComparison;
        }

        var minorComparison = Minor.CompareTo(other.Minor);
        if (minorComparison != 0) {
            return minorComparison;
        }

        return Patch.CompareTo(other.Patch);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    private static (int Major, int Minor, int Patch) Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) {
            throw new ArgumentException("Version string cannot be null or empty.", nameof(value));
        }

        var parts = value.Split('.');
        if (parts.Length != 3) {
            throw new ArgumentException("Version must follow SemVer format: Major.Minor.Patch", nameof(value));
        }

        if (!int.TryParse(parts[0], out var major) || major < 0 ||
            !int.TryParse(parts[1], out var minor) || minor < 0 ||
            !int.TryParse(parts[2], out var patch) || patch < 0) {
            throw new ArgumentException("Version parts must be non-negative integers.", nameof(value));
        }

        return (major, minor, patch);
    }
}