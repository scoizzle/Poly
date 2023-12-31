namespace Poly;

public partial struct StringSlice :
    IComparable<StringSlice>,
    IComparable<string>,
    IComparable<char[]>,
    IEquatable<StringSlice>,
    IEquatable<string>,
    IEquatable<char[]>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly override bool Equals(object? obj) => obj switch {
        StringSlice slice => CompareTo(slice) == 0,
        string str => CompareTo(str) == 0,
        char[] arr => CompareTo(arr) == 0,
        _ => false
    };
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool Equals(StringSlice other) => CompareTo(other.AsSpan()) == 0;

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
    public readonly int CompareTo(StringSlice other) => CompareTo(other.String, other.Begin, other.End);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int CompareTo(StringSlice other, StringComparison comparison) {
        var sameInstance = 
            (Begin, End) == (other.Begin, other.End) && 
            ReferenceEquals(String, other.String);

        return (BoundsCheck(), other.BoundsCheck(), sameInstance) switch {
            (true, true, false) => AsSpan().CompareTo(other.AsSpan(), comparison),
            (true, true, true) => 0,
            (false, false, _) => 0,
            (true, false, _) => 1,
            (false, true, _) => -1,
        };
    }        

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int CompareTo(string? otherString, int otherBegin, int otherEnd) {
        if (String is null || otherString is null)
            return 0;

        if (Begin == otherBegin && End == otherEnd && ReferenceEquals(String, otherString))
            return 0;

        var length = Length;
        var otherLength = otherEnd - otherBegin;

        var diff = length - otherLength;
        if (diff != 0)
            return diff;

        return String.AsSpan(Begin, length).SequenceCompareTo(otherString.AsSpan(otherBegin, otherLength));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool IsAt(char character) {
        return Current == character;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool IsAt(string subString, StringComparison comparison = StringComparison.Ordinal) {
        if (!BoundsCheck(subString))
            return false;

        ReadOnlySpan<char> currentSpan = AsSpan();
        ReadOnlySpan<char> subSpan = subString.AsSpan();

        return currentSpan.StartsWith(subSpan, comparison);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool IsAt(string subString, int subIndex, int length, StringComparison comparison = StringComparison.Ordinal) {
        if (!BoundsCheck(subString, subIndex, length))
            return false;

        ReadOnlySpan<char> currentSpan = AsSpan();
        ReadOnlySpan<char> subSpan = subString.AsSpan(subIndex, length);

        return currentSpan.StartsWith(subSpan, comparison);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool IsAt(in StringSlice slice, StringComparison comparison = StringComparison.Ordinal) {
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

        char? current = Current;

        if (!current.HasValue)
            return false;

        return predicate(current.Value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool IsAt(params Func<char, bool>[] predicates) {
        Guard.IsNotNull(predicates);

        char? current = Current;

        if (!current.HasValue)
            return false;

        return predicates.Any(p => p(current.Value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool IsNotAt(Func<char, bool> predicate)
    {
        Guard.IsNotNull(predicate);

        var current = Current;

        if (!current.HasValue)
            return false;

        return !predicate(current.Value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool IsNotAt(params Func<char, bool>[] predicates) {
        Guard.IsNotNull(predicates);

        char? current = Current;

        if (!current.HasValue)
            return false;

        return !predicates.Any(p => p(current.Value));
    }
}