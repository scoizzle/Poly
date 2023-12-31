namespace Poly;

public partial struct StringSlice
{
    public StringSlice(string str)
    {
        Guard.IsNotNull(str);

        (String, Begin, End) = (str, 0, str.Length);
    }

    public StringSlice(string str, int begin)
    {
        Guard.IsNotNull(str);
        Guard.IsBetweenOrEqualTo(begin, 0, str.Length);

        (String, Begin, End) = (str, begin, str.Length);
    }

    public StringSlice(string str, int begin, int end)
    {
        Guard.IsNotNull(str);
        Guard.IsBetweenOrEqualTo(begin, 0, end);
        Guard.IsBetweenOrEqualTo(end, begin, str.Length);

        (String, Begin, End) = (str, begin, end);
    }
    
    public readonly string String { get; init; }

    public int Begin { readonly get; set; }

    public int End { readonly get; set; }

    public readonly int Length {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => End - Begin;
    }

    public readonly bool IsEmpty {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Begin == End;
    }

    public readonly char? Current {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => BoundsCheck()
            ? String[Begin]
            : default;
    }

    public readonly char? this[int index] {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => BoundsCheck(index - Begin)
            ? String[index]
            : default;
    }
    
    public readonly StringSlice this[int begin, int end] {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(String, begin, end);
    }

    public readonly StringSlice this[Range range] {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get {
            var (start, length) = range.GetOffsetAndLength(End - Begin);

            var begin = Begin + start;

            return new(String, begin, begin + length);
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool BoundsCheck()
        => String != null
        && Begin >= 0
        && End <= String.Length;
        
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool BoundsCheck(int length)
        => String != null
        && Begin >= 0 
        && Begin + length <= End 
        && End <= String.Length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool BoundsCheck(string subString)
        => String != null
        && subString != null
        && Begin >= 0 
        && Begin + subString.Length <= End
        && End <= String.Length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool BoundsCheck(string subString, int subIndex, int length)
        => String != null
        && subString != null
        && Begin >= 0 
        && Begin + length <= End
        && End <= String.Length
        && subIndex >= 0
        && subIndex + length <= subString.Length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool BoundsCheck(StringSlice other)
        => String != null
        && other.String != null
        && Begin >= 0 
        && Begin + other.Length <= End
        && End <= String.Length
        && other.Begin >= 0
        && other.End <= other.String.Length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool ReferenceEquals(StringSlice other) {
        if (!BoundsCheck(other))
            return false;
            
        var (otherString, otherBegin, otherEnd) = other;

        return (Begin, End) == (otherBegin, otherEnd) && ReferenceEquals(String, otherString);        
    }

    public readonly ReadOnlySpan<char> AsSpan() => String.AsSpan(Begin, Length);

    public readonly void Deconstruct(out string str, out int begin, out int end) => (str, begin, end) = (String, Begin, End);
    
    public readonly override int GetHashCode() => unchecked(String.GetHashCode() ^ (Begin << 24) + (End << 8));
    public readonly override string ToString() => String.Substring(Begin, Length);
    
    public static implicit operator StringSlice(string str) => new(str);

    public static bool operator ==(StringSlice left, StringSlice right) => left.CompareTo(right, StringComparison.CurrentCulture) == 0;

    public static bool operator !=(StringSlice left, StringSlice right) => left.CompareTo(right, StringComparison.CurrentCulture) != 0;
}