namespace Poly;

public partial struct StringView :
    IComparable<StringView>,
    IComparable<string>,
    IComparable<char[]>,
    IEquatable<StringView>,
    IEquatable<string>,
    IEquatable<char[]>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly override bool Equals(object? obj) => obj switch
    {
        StringView slice => CompareTo(slice) == 0,
        string str => CompareTo(str) == 0,
        char[] arr => CompareTo(arr) == 0,
        _ => false
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool Equals(StringView other) => CompareTo(other.AsSpan()) == 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool Equals(string? other) => CompareTo(other.AsSpan()) == 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool Equals(char[]? other) => CompareTo(other.AsSpan()) == 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int CompareTo(string? other) => CompareTo(other.AsSpan());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int CompareTo(char[]? other) => CompareTo(other.AsSpan());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int CompareTo(ReadOnlySpan<char> other)
        => BoundsCheck()
            ? AsSpan().SequenceCompareTo(other)
            : -1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int CompareTo(StringView other) => CompareTo(other, StringComparison.Ordinal);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int CompareTo(StringView other, StringComparison comparison)
    {
        var (strA, idxA, lstA) = this;
        var (strB, idxB, lstB) = other;

        if (String is null || strB is null)
            return 0;

        if (idxA == idxB && lstA == lstB && ReferenceEquals(strA, strB))
            return 0;

        var lenA = lstA - idxA;
        var lenB = lstB - idxB;

        var spanA = strA.AsSpan(idxA, lenA);
        var spanB = strB.AsSpan(idxB, lenB);

        return spanA.CompareTo(spanB, comparison);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int CompareTo(string? strB, int idxB, int lstB)
    {
        var (strA, idxA, lstA) = this;

        if (String is null || strB is null)
            return 0;

        if (idxA == idxB && lstA == lstB && ReferenceEquals(strA, strB))
            return 0;

        var lenA = lstA - idxA;
        var lenB = lstB - idxB;

        var spanA = strA.AsSpan(idxA, lenA);
        var spanB = strB.AsSpan(idxB, lenB);

        return spanA.CompareTo(spanB, StringComparison.Ordinal);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool IsAt(char character)
    {
        return First == character;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool IsAt(string subString, StringComparison comparison = StringComparison.Ordinal)
    {
        if (!BoundsCheck(subString))
            return false;

        ReadOnlySpan<char> currentSpan = AsSpan();
        ReadOnlySpan<char> subSpan = subString.AsSpan();

        return currentSpan.StartsWith(subSpan, comparison);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool IsAt(string subString, int subIndex, int length, StringComparison comparison = StringComparison.Ordinal)
    {
        if (!BoundsCheck(subString, subIndex, length))
            return false;

        ReadOnlySpan<char> currentSpan = AsSpan();
        ReadOnlySpan<char> subSpan = subString.AsSpan(subIndex, length);

        return currentSpan.StartsWith(subSpan, comparison);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool IsAt(StringView slice, StringComparison comparison = StringComparison.Ordinal)
    {
        if (!BoundsCheck() || !slice.BoundsCheck())
            return false;

        ReadOnlySpan<char> currentSpan = AsSpan();
        ReadOnlySpan<char> subSpan = slice.AsSpan();

        return currentSpan.StartsWith(subSpan, comparison);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool IsAt(Func<char, bool> predicate)
    {
        Guard.IsNotNull(predicate);

        char? current = First;

        if (!current.HasValue)
            return false;

        return predicate(current.Value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool IsAt(params Func<char, bool>[] predicates)
    {
        Guard.IsNotNull(predicates);

        char? current = First;

        if (!current.HasValue)
            return false;


        foreach (var predicate in predicates)
        {
            if (predicate(current.Value))
                return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool IsNotAt(Func<char, bool> predicate)
    {
        Guard.IsNotNull(predicate);

        var current = First;

        if (!current.HasValue)
            return false;

        return !predicate(current.Value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool IsNotAt(params Func<char, bool>[] predicates)
    {
        Guard.IsNotNull(predicates);

        char? current = First;

        if (!current.HasValue)
            return false;

        foreach (var predicate in predicates)
        {
            if (predicate(current.Value))
                return false;
        }

        return true;
    }
}