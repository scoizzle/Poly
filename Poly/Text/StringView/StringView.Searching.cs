namespace Poly;

public partial struct StringView
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int IndexOf(
        char value,
        StringComparison comparisonType = StringComparison.Ordinal)
    {
        return StringSearching.IndexOf(String, Index, LastIndex, value, comparisonType);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int IndexOf(
        string value,
        StringComparison comparisonType = StringComparison.Ordinal)
    {
        return StringSearching.IndexOf(String, Index, LastIndex, value, comparisonType);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int IndexOf(
        string subString,
        int subIndex,
        int length,
        StringComparison comparisonType = StringComparison.Ordinal)
    {
        return StringSearching.IndexOf(String, Index, LastIndex, subString, subIndex, length, comparisonType);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int IndexOf(
        StringView slice,
        StringComparison comparisonType = StringComparison.Ordinal)
    {
        return StringSearching.IndexOf(String, Index, LastIndex, slice.String, slice.Index, slice.Length, comparisonType);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly IEnumerable<int> FindAll(
        char value,
        StringComparison comparisonType = StringComparison.Ordinal)
    {
        return StringSearching.FindAll(String, Index, LastIndex, value, comparisonType);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly IEnumerable<int> FindAll(
        string value,
        StringComparison comparisonType = StringComparison.Ordinal)
    {
        return StringSearching.FindAll(String, Index, LastIndex, value, comparisonType);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly IEnumerable<int> FindAll(
        string subString,
        int subIndex,
        int length,
        StringComparison comparisonType = StringComparison.Ordinal)
    {
        return StringSearching.FindAll(String, Index, LastIndex, subString, subIndex, length, comparisonType);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly IEnumerable<int> FindAll(
        StringView slice,
        StringComparison comparisonType = StringComparison.Ordinal)
    {
        return StringSearching.FindAll(String, Index, LastIndex, slice.String, slice.Index, slice.Length, comparisonType);
    }
}