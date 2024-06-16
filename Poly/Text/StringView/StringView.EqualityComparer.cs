namespace Poly;

public readonly struct StringViewEqualityComparer : IEqualityComparer<StringView>
{
    public readonly StringComparison Comparison;

    public StringViewEqualityComparer(StringComparison comparison = StringComparison.Ordinal)
        => Comparison = comparison;

    public bool Equals(StringView x, StringView y)
        => x.CompareTo(y, Comparison) == 0;

    public int GetHashCode(StringView obj)
        => obj.GetHashCode();

    public static readonly StringViewEqualityComparer Ordinal = new(StringComparison.Ordinal);

    public static readonly StringViewEqualityComparer OrdinalIgnoreCase = new(StringComparison.OrdinalIgnoreCase);

    public static readonly StringViewEqualityComparer CurrentCulture = new(StringComparison.CurrentCulture);

    public static readonly StringViewEqualityComparer CurrentCultureIgnoreCase = new(StringComparison.CurrentCultureIgnoreCase);

    public static readonly StringViewEqualityComparer InvariantCulture = new(StringComparison.InvariantCulture);

    public static readonly StringViewEqualityComparer InvariantCultureIgnoreCase = new(StringComparison.InvariantCultureIgnoreCase);
}