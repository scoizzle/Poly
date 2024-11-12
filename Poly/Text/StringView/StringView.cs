namespace Poly;

[DebuggerDisplay("{DebuggerDisplay}")]
public partial struct StringView
{
    public static readonly StringView Empty = new(string.Empty);

    public StringView(string str)
    {
        Guard.IsNotNull(str);

        (String, Index, LastIndex) = (str, 0, str.Length);
    }

    public StringView(string str, int begin)
    {
        Guard.IsNotNull(str);
        Guard.IsBetweenOrEqualTo(begin, 0, str.Length);

        (String, Index, LastIndex) = (str, begin, str.Length);
    }

    public StringView(string str, int begin, int end)
    {
        Guard.IsNotNull(str);
        Guard.IsBetweenOrEqualTo(begin, 0, end);
        Guard.IsBetweenOrEqualTo(end, begin, str.Length);

        (String, Index, LastIndex) = (str, begin, end);
    }

    public readonly string String { get; init; }

    public int Index { readonly get; set; }

    public int LastIndex { readonly get; set; }

    public readonly int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => LastIndex - Index;
    }

    public readonly bool IsEmpty
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Index == LastIndex;
    }

    public readonly char? First
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => BoundsCheck()
            ? String[Index]
            : default;
    }

    public readonly char? Last
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => BoundsCheck()
            ? String[LastIndex - 1]
            : default;
    }

    public readonly char? this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            return BoundsCheck(index)
                ? String[Index + index]
                : default;
        }
    }

    public readonly StringView this[int begin, int end]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(String, begin, end);
    }

    public readonly StringView this[Range range]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var (start, length) = range.GetOffsetAndLength(LastIndex - Index);

            var begin = Index + start;

            return new(String, begin, begin + length);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool BoundsCheck() => String.BoundsCheck(Index, LastIndex);


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool BoundsCheck(int length) => String.BoundsCheck(Index, LastIndex, length);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool BoundsCheck(string subString) => String.BoundsCheck(Index, LastIndex, subString);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool BoundsCheck(string subString, int subIndex, int length) => String.BoundsCheck(Index, LastIndex, subString, subIndex, length);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool BoundsCheck(StringView other) => String.BoundsCheck(Index, LastIndex, other.String, other.Index, other.Length);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool ReferenceEquals(StringView other)
    {
        if (!BoundsCheck(other))
            return false;

        var (otherString, otherBegin, otherEnd) = other;

        return (Index, LastIndex) == (otherBegin, otherEnd) && ReferenceEquals(String, otherString);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ReadOnlySpan<char> AsSpan() => String.AsSpan(Index, Length);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ReadOnlySpan<char> AsSpan(int length) => String.AsSpan(Index, length);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void Deconstruct(out string str, out int begin, out int end) => (str, begin, end) = (String, Index, LastIndex);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly override int GetHashCode() => HashCode.Combine(First, Length);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly override string ToString() => AsSpan().ToString();
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator StringView(string str) => new(str);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator ReadOnlySpan<char>(StringView slice) => slice.AsSpan();

    public static bool operator ==(StringView left, StringView right) => left.CompareTo(right, StringComparison.CurrentCulture) == 0;

    public static bool operator !=(StringView left, StringView right) => left.CompareTo(right, StringComparison.CurrentCulture) != 0;

    private readonly string DebuggerDisplay => ToString();
}