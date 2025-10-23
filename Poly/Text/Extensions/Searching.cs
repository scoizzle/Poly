namespace Poly;

public static class StringSearching {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int IndexOf(this string This, int index, int lastIndex, char value, StringComparison comparisonType = StringComparison.Ordinal) {
        if (!StringIteration.BoundsCheck(This, index, lastIndex))
            return -1;

        ReadOnlySpan<char> currentSpan = This.AsSpan(index, lastIndex - index);
        ReadOnlySpan<char> subSpan = [value];

        var idx = currentSpan.IndexOf(subSpan, comparisonType);

        if (idx == -1)
            return -1;

        return idx;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int IndexOf(this string This, int index, int lastIndex, string subString, StringComparison comparisonType = StringComparison.Ordinal) {
        if (ReferenceEquals(This, subString) && index == 0 && lastIndex == subString.Length)
            return 0;

        if (!StringIteration.BoundsCheck(This, index, lastIndex, subString))
            return -1;

        ReadOnlySpan<char> currentSpan = This.AsSpan(index, lastIndex - index);
        ReadOnlySpan<char> subSpan = subString.AsSpan();

        var idx = currentSpan.IndexOf(subSpan, comparisonType);

        if (idx == -1)
            return -1;

        return idx;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int IndexOf(this string This, int index, int lastIndex, string subString, int subIndex, int length, StringComparison comparisonType = StringComparison.Ordinal) {
        if (ReferenceEquals(This, subString) && index <= subIndex && lastIndex >= subIndex + length)
            return subIndex;

        if (!StringIteration.BoundsCheck(This, index, lastIndex, subString, subIndex, length))
            return -1;

        ReadOnlySpan<char> currentSpan = This.AsSpan(index, lastIndex - index);
        ReadOnlySpan<char> subSpan = subString.AsSpan(subIndex, length);

        var idx = currentSpan.IndexOf(subSpan, comparisonType);

        if (idx == -1)
            return -1;

        return idx;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int IndexOfAny(this string This, int index, int lastIndex, ReadOnlySpan<char> characters, StringComparison comparisonType = StringComparison.Ordinal) {
        if (!StringIteration.BoundsCheck(This, index, lastIndex))
            return -1;

        var length = lastIndex - index;
        var currentSpan = This.AsSpan(index, lastIndex - index);

        if (comparisonType == StringComparison.Ordinal) {
            for (var i = 0; i < currentSpan.Length; i++) {
                var chr = currentSpan[i];

                if (characters.Contains(chr))
                    return index + i;
            }

            return -1;
        }

        var idx = lastIndex;

        for (var i = 0; i < characters.Length; i++) {
            var chr = characters.Slice(i, 1);

            var next = currentSpan.IndexOf(chr, comparisonType);

            if (next == -1)
                continue;

            if (next < idx)
                idx = next;
        }

        return idx == lastIndex
            ? -1
            : idx;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IEnumerable<int> FindAll(this string This, int index, int lastIndex, char value, StringComparison comparisonType = StringComparison.Ordinal) {
        while (StringIteration.BoundsCheck(This, index, lastIndex)) {
            index = IndexOf(This, index, lastIndex, value, comparisonType);

            if (index == -1)
                yield break;

            yield return index++;
        }
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IEnumerable<int> FindAll(this string This, int index, int lastIndex, string subString, StringComparison comparisonType = StringComparison.Ordinal) {
        while (StringIteration.BoundsCheck(This, index, lastIndex, subString)) {
            index = IndexOf(This, index, lastIndex, subString, comparisonType);

            if (index == -1)
                yield break;

            yield return index;

            index += subString.Length;
        }
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IEnumerable<int> FindAll(this string This, int index, int lastIndex, string subString, int subIndex, int length, StringComparison comparisonType = StringComparison.Ordinal) {
        while (StringIteration.BoundsCheck(This, index, lastIndex, subString, subIndex, length)) {
            index = IndexOf(This, index, lastIndex, subString, subIndex, length, comparisonType);

            if (index == -1)
                yield break;

            yield return index;

            index += subString.Length;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int FindMatchingBracket(
        this string This,
        int index,
        int lastIndex,
        char open,
        char close
        ) {
        if (!StringIteration.Consume(This, ref index, lastIndex, open))
            return -1;

        var count = 1;

        while (index < lastIndex) {
            var character = This[index];

            if (character == '\\')
                index++;
            else
            if (character == close && --count == 0)
                return index;
            else
            if (character == open)
                count++;

            index++;
        }

        return -1;
    }
}