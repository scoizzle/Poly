namespace Poly;

public readonly struct StringSliceEqualityComparer : IEqualityComparer<StringSlice>
{
    public readonly StringComparison Comparison;

    public StringSliceEqualityComparer(StringComparison comparison = StringComparison.Ordinal)
        => Comparison = comparison;

    public bool Equals(StringSlice x, StringSlice y)
        => x.CompareTo(y, Comparison) == 0;

    public int GetHashCode(StringSlice obj)
        => obj.GetHashCode();

    public static readonly StringSliceEqualityComparer Ordinal = new(StringComparison.Ordinal);

    public static readonly StringSliceEqualityComparer OrdinalIgnoreCase = new(StringComparison.OrdinalIgnoreCase);

    public static readonly StringSliceEqualityComparer CurrentCulture = new(StringComparison.CurrentCulture);

    public static readonly StringSliceEqualityComparer CurrentCultureIgnoreCase = new(StringComparison.CurrentCultureIgnoreCase);

    public static readonly StringSliceEqualityComparer InvariantCulture = new(StringComparison.InvariantCulture);

    public static readonly StringSliceEqualityComparer InvariantCultureIgnoreCase = new(StringComparison.InvariantCultureIgnoreCase);
}