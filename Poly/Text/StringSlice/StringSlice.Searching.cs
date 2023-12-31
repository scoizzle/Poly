
using System.Runtime.Intrinsics.Arm;

namespace Poly;

public partial struct StringSlice
{    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int IndexOf(char chr, StringComparison stringComparison = StringComparison.Ordinal) {
        if (!BoundsCheck(1))
            return -1;

        ReadOnlySpan<char> currentSpan = AsSpan();
        ReadOnlySpan<char> subSpan = stackalloc[] { chr };

        var idx = currentSpan.IndexOf(subSpan, stringComparison);

        if (idx == -1)
            return -1;

        return Begin + idx;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int IndexOf(
        string subString, 
        StringComparison stringComparison = StringComparison.Ordinal) 
    {
        if (!BoundsCheck(subString))
            return -1;

        if ((Begin, Length) == (0, subString.Length) && ReferenceEquals(String, subString))
            return 0;
        
        ReadOnlySpan<char> currentSpan = AsSpan();
        ReadOnlySpan<char> subSpan = subString.AsSpan();

        var idx = currentSpan.IndexOf(subSpan, stringComparison);

        if (idx == -1)
            return -1;

        return Begin + idx;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int IndexOf(
        string subString, 
        int subIndex, 
        int length, 
        StringComparison stringComparison = StringComparison.Ordinal) 
    {
        if (!BoundsCheck(subString, subIndex, length))
            return -1;

        if ((Begin, Length) == (subIndex, length) && ReferenceEquals(String, subString))
            return 0;
        
        ReadOnlySpan<char> currentSpan = AsSpan();
        ReadOnlySpan<char> subSpan = subString.AsSpan(subIndex, length);

        var idx = currentSpan.IndexOf(subSpan, stringComparison);
        
        if (idx == -1)
            return -1;

        return Begin + idx;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int IndexOf(
        StringSlice slice, 
        StringComparison stringComparison = StringComparison.Ordinal) 
    {
        if (!BoundsCheck(slice))
            return -1;
        
        ReadOnlySpan<char> currentSpan = AsSpan();
        ReadOnlySpan<char> subSpan = slice.AsSpan();

        var idx = currentSpan.IndexOf(subSpan, stringComparison);
        
        if (idx == -1)
            return -1;

        return Begin + idx;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly IEnumerable<int> FindAll(
        char chr, 
        StringComparison stringComparison = StringComparison.Ordinal) 
    {
        if (!BoundsCheck(1))
            yield break;

        var (str, begin, end) = this;

        do {
            begin = str
                .AsSpan(begin, end - begin)
                .IndexOf(stackalloc[] { chr }, stringComparison);

            if (begin == -1)
                yield break;

            yield return begin++;
        }
        while (true);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly IEnumerable<int> FindAll(
        string text, 
        StringComparison stringComparison = StringComparison.Ordinal) 
    {
        if (!BoundsCheck(text))
            return Enumerable.Empty<int>();

        return FindAllInternal(text, stringComparison);
    }
        
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly IEnumerable<int> FindAll(
        string text, 
        int index, 
        int length, 
        StringComparison stringComparison = StringComparison.Ordinal)
    {
        if (!BoundsCheck(text, index, length))
            return Enumerable.Empty<int>();

        return FindAllInternal(text.AsSlice(index, length), stringComparison);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly IEnumerable<int> FindAll(
        StringSlice slice, 
        StringComparison stringComparison = StringComparison.Ordinal) 
    {
        if (!BoundsCheck() || !slice.BoundsCheck())
            return Enumerable.Empty<int>();

        return FindAllInternal(slice, stringComparison);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly IEnumerable<int> FindAllInternal(
        StringSlice view, 
        StringComparison stringComparison = StringComparison.Ordinal) 
    {
        var length = view.Length;
        var current = this;

        do {
            var idx = current.IndexOf(view, stringComparison);

            if (idx == -1)
                break;

            yield return idx;

            current.Begin = idx + length;
        }
        while (!current.IsEmpty);

        yield return current.Begin;
    }
}