namespace Poly;

public static class StringIteration
{

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool BoundsCheck(this string This, int index)
        => This != null
        && index >= 0
        && index <= This.Length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool BoundsCheck(this string This, int index, int lastIndex)
        => This != null
        && index >= 0
        && index <= lastIndex
        && lastIndex <= This.Length;


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool BoundsCheck(this string This, int index, int lastIndex, int length)
        => This != null
        && index >= 0
        && index + length <= lastIndex
        && lastIndex <= This.Length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool BoundsCheck(this string This, int index, int lastIndex, string subString)
        => This != null
        && subString != null
        && index >= 0
        && index + subString.Length <= lastIndex
        && lastIndex <= This.Length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool BoundsCheck(this string This, int index, int lastIndex, string subString, int subIndex, int length)
        => This != null
        && subString != null
        && index + length >= 0
        && index + length <= lastIndex
        && lastIndex <= This.Length
        && subIndex >= 0
        && subIndex + length <= subString.Length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsAt(this string This, int index, char value, StringComparison comparisonType = StringComparison.Ordinal)
    {
        if (!BoundsCheck(This, index))
            return false;

        ReadOnlySpan<char> strA = This.AsSpan(index, 1);
        ReadOnlySpan<char> strB = [value];

        return strA.CompareTo(strB, comparisonType) == 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsAt(this string This, int index, int lastIndex, char value, StringComparison comparisonType = StringComparison.Ordinal)
    {
        if (!BoundsCheck(This, index, lastIndex))
            return false;

        ReadOnlySpan<char> strA = This.AsSpan(index, 1);
        ReadOnlySpan<char> strB = [value];

        return strA.CompareTo(strB, comparisonType) == 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsAt(this string This, int index, int lastIndex, string subString, StringComparison comparisonType = StringComparison.Ordinal)
    {
        if (!BoundsCheck(This, index, lastIndex, subString))
            return false;

        ReadOnlySpan<char> strA = This.AsSpan(index, subString.Length);
        ReadOnlySpan<char> strB = subString;

        return strA.CompareTo(strB, comparisonType) == 0;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsAt(this string This, int index, int lastIndex, string subString, int subIndex, int length, StringComparison comparisonType = StringComparison.Ordinal)
    {
        if (!BoundsCheck(This, index, lastIndex, subString, subIndex, length))
            return false;

        ReadOnlySpan<char> strA = This.AsSpan(index, length);
        ReadOnlySpan<char> strB = subString.AsSpan(subIndex, length);

        return strA.CompareTo(strB, comparisonType) == 0;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsAt(this string This, int index, int lastIndex, Func<char, bool> predicate)
        => BoundsCheck(This, index, lastIndex)
        && predicate(This[index]);


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsAt(this string This, int index, int lastIndex, params Func<char, bool>[] predicates)
        => BoundsCheck(This, index, lastIndex)
        && predicates.Any(f => f(This[index]));


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNotAt(this string This, int index, int lastIndex, Func<char, bool> predicate)
        => BoundsCheck(This, index, lastIndex)
        && !predicate(This[index]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNotAt(this string This, int index, int lastIndex, params Func<char, bool>[] predicates)
        => BoundsCheck(This, index, lastIndex)
        && !predicates.Any(f => f(This[index]));


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Equals(this string This, int index, int lastIndex, string subString, StringComparison comparisonType = StringComparison.Ordinal)
    {
        if (!BoundsCheck(This, index, lastIndex, subString) || subString.Length != lastIndex - index)
            return false;

        ReadOnlySpan<char> strA = This.AsSpan(index, subString.Length);
        ReadOnlySpan<char> strB = subString;

        return strA.CompareTo(strB, comparisonType) == 0;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Equals(this string This, int index, int lastIndex, string subString, int subIndex, int length, StringComparison comparisonType = StringComparison.Ordinal)
    {
        if (!BoundsCheck(This, index, lastIndex, subString, subIndex, length) || length != lastIndex - index)
            return false;

        ReadOnlySpan<char> strA = This.AsSpan(index, subString.Length);
        ReadOnlySpan<char> strB = subString.AsSpan(subIndex, length);

        return strA.CompareTo(strB, comparisonType) == 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Consume(this string This, ref int index, int lastIndex, int n)
    {
        if (BoundsCheck(This, index, lastIndex, n))
        {
            index += n;
            return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Consume(this string This, ref int index, char value, StringComparison comparisonType = StringComparison.Ordinal)
    {
        if (IsAt(This, index, value, comparisonType))
        {
            index++;
            return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Consume(this string This, ref int index, int lastIndex, char character, StringComparison comparisonType = StringComparison.Ordinal)
    {
        if (IsAt(This, index, lastIndex, character, comparisonType))
        {
            index++;
            return true;
        }

        return false;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Consume(this string This, ref int index, int lastIndex, out char character)
    {
        if (BoundsCheck(This, index, lastIndex))
        {
            character = This[index++];
            return true;
        }

        character = default;
        return false;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Consume(this string This, ref int index, int lastIndex, out int digit)
    {
        if (BoundsCheck(This, index, lastIndex))
        {
            var character = This[index];
            if ((character ^ '0') > 9)
            {
                digit = character - '0';
                index++;
                return true;
            }
        }

        digit = default;
        return false;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Consume(this string This, ref int index, int lastIndex, string subString, StringComparison comparisonType = StringComparison.Ordinal)
    {
        if (IsAt(This, index, lastIndex, subString, comparisonType))
        {
            index += subString.Length;
            return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Consume(this string This, ref int index, int lastIndex, string subString, int subIndex, int length, StringComparison comparisonType = StringComparison.Ordinal)
    {
        if (IsAt(This, index, lastIndex, subString, subIndex, length, comparisonType))
        {
            index += length;
            return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Consume(this string This, ref int index, int lastIndex, Func<char, bool> predicate)
    {
        if (!BoundsCheck(This, index, lastIndex))
            return false;

        var offset = index;

        while (offset < lastIndex)
        {
            if (!predicate(This[offset]))
                break;

            offset++;
        }

        if (offset == index)
            return false;

        index = offset;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Consume(this string This, ref int index, int lastIndex, params Func<char, bool>[] predicates)
    {
        if (!BoundsCheck(This, index, lastIndex))
            return false;

        var offset = index;

        while (offset < lastIndex)
        {
            var character = This[offset];
            var precondition = false;

            for (var i = 0; i < predicates.Length; i++)
            {
                if (precondition = predicates[i](character))
                    break;
            }

            if (!precondition)
                break;

            offset++;
        }


        if (offset == index)
            return false;

        index = offset;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ConsumeUntil(this string This, ref int index, int lastIndex, Func<char, bool> predicate)
    {
        if (!BoundsCheck(This, index, lastIndex))
            return false;

        var offset = index;

        while (offset < lastIndex)
        {
            if (predicate(This[offset]))
                break;

            offset++;
        }

        if (offset == index)
            return false;

        index = offset;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ConsumeUntil(this string This, ref int index, int lastIndex, params Func<char, bool>[] predicates)
    {
        if (!BoundsCheck(This, index, lastIndex))
            return false;

        var offset = index;

        while (offset < lastIndex)
        {
            var character = This[offset];
            var precondition = false;

            for (var i = 0; i < predicates.Length; i++)
            {
                if (precondition = predicates[i](character))
                    break;
            }

            if (precondition)
                break;

            offset++;
        }


        if (offset == index)
            return false;

        index = offset;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Goto(this string This, ref int index, int lastIndex, char character, StringComparison comparisonType = StringComparison.Ordinal)
    {
        if (!BoundsCheck(This, index, lastIndex))
            return false;

        var location = StringSearching.IndexOf(This, index, lastIndex, character, comparisonType);

        if (location == -1)
            return false;

        index = location;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Goto(this string This, ref int index, int lastIndex, string subString, StringComparison comparisonType = StringComparison.Ordinal)
    {
        if (!BoundsCheck(This, index, lastIndex, subString))
            return false;

        var location = StringSearching.IndexOf(This, index, lastIndex, subString, comparisonType);

        if (location == -1)
            return false;

        index = location;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Goto(this string This, ref int index, int lastIndex, string subString, int subIndex, int length, StringComparison comparisonType = StringComparison.Ordinal)
    {
        if (!BoundsCheck(This, index, lastIndex, subString, subIndex, length))
            return false;

        var location = StringSearching.IndexOf(This, index, lastIndex, subString, subIndex, length, comparisonType);

        if (location == -1)
            return false;

        index = location;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool GotoAny(this string This, ref int index, int lastIndex, ReadOnlySpan<char> characters, StringComparison comparisonType = StringComparison.Ordinal)
    {
        if (!BoundsCheck(This, index, lastIndex))
            return false;

        var location = StringSearching.IndexOfAny(This, index, lastIndex, characters, comparisonType);

        if (location == -1)
            return false;

        index = location;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool GotoAndConsume(this string This, ref int index, int lastIndex, char character, StringComparison comparisonType = StringComparison.Ordinal)
    {
        if (!BoundsCheck(This, index, lastIndex))
            return false;

        var location = StringSearching.IndexOf(This, index, lastIndex, character, comparisonType);

        if (location == -1)
            return false;

        index = location + 1;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool GotoAndConsume(this string This, ref int index, int lastIndex, string subString, StringComparison comparisonType = StringComparison.Ordinal)
    {
        if (!BoundsCheck(This, index, lastIndex, subString))
            return false;

        var location = StringSearching.IndexOf(This, index, lastIndex, subString, comparisonType);

        if (location == -1)
            return false;

        index = location + subString.Length;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool GotoAndConsume(this string This, ref int index, int lastIndex, string subString, int subIndex, int length, StringComparison comparisonType = StringComparison.Ordinal)
    {
        if (!BoundsCheck(This, index, lastIndex, subString, subIndex, length))
            return false;

        var location = StringSearching.IndexOf(This, index, lastIndex, subString, subIndex, length, comparisonType);

        if (location == -1)
            return false;

        index = location + length;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool GotoAndConsume(this string This, ref int index, int lastIndex, Func<char, bool> predicate)
    {
        var offset = index;

        while (IsNotAt(This, offset, lastIndex, predicate))
            offset++;

        if (offset == index || offset == lastIndex)
            return false;

        while (IsAt(This, offset, lastIndex, predicate))
            offset++;

        index = offset;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool GotoAndConsume(this string This, ref int index, int lastIndex, params Func<char, bool>[] predicates)
    {
        var offset = index;

        while (IsNotAt(This, offset, lastIndex, predicates))
            offset++;

        if (offset == index || offset == lastIndex)
            return false;

        while (IsAt(This, offset, lastIndex, predicates))
            offset++;

        index = offset;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool GotoAndConsumeAny(this string This, ref int index, int lastIndex, char[] characters, StringComparison comparisonType = StringComparison.Ordinal)
    {
        if (!BoundsCheck(This, index, lastIndex))
            return false;

        var location = StringSearching.IndexOfAny(This, index, lastIndex, characters, comparisonType);

        if (location == -1)
            return false;

        index = location + 1;
        return true;
    }
}