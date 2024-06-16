namespace Poly;

public partial struct StringView
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Consume(int numberOfCharacters = 1)
    {
        if (!BoundsCheck(numberOfCharacters))
            return false;

        Index += numberOfCharacters;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Consume(out char character)
    {
        var (str, index, lastIndex) = this;

        if (str.Consume(ref index, lastIndex, out character))
        {
            Index = index;
            return true;
        }

        character = default;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Consume(char value, StringComparison comparisonType = StringComparison.Ordinal)
    {
        var (str, index, lastIndex) = this;

        if (str.Consume(ref index, lastIndex, value, comparisonType))
        {
            Index = index;
            return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Consume(string subString, StringComparison comparisonType = StringComparison.Ordinal)
    {
        var (str, index, lastIndex) = this;

        if (str.Consume(ref index, lastIndex, subString, comparisonType))
        {
            Index = index;
            return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Consume(string subString, int subIndex, int length, StringComparison comparisonType = StringComparison.Ordinal)
    {
        var (str, index, lastIndex) = this;

        if (str.Consume(ref index, lastIndex, subString, comparisonType))
        {
            Index = index;
            return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Consume(StringView sub, StringComparison comparisonType = StringComparison.Ordinal)
    {
        var (str, idx, lst) = this;
        var (subStr, subIdx, subLst) = sub;

        if (ReferenceEquals(str, subStr) && idx == subIdx && subLst <= lst)
        {
            Index = subLst;
            return true;
        }

        if (str.Consume(ref idx, lst, sub.String, comparisonType))
        {
            Index = idx;
            return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Consume(Func<char, bool> predicate)
    {
        var (str, index, lastIndex) = this;

        if (str.Consume(ref index, lastIndex, predicate))
        {
            Index = index;
            return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Consume(params Func<char, bool>[] predicates)
    {
        var (str, index, lastIndex) = this;

        if (str.Consume(ref index, lastIndex, predicates))
        {
            Index = index;
            return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ConsumeUntil(Func<char, bool> predicate)
    {
        var (str, index, lastIndex) = this;

        if (str.ConsumeUntil(ref index, lastIndex, predicate))
        {
            Index = index;
            return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ConsumeUntil(params Func<char, bool>[] predicates)
    {
        var (str, index, lastIndex) = this;

        if (str.ConsumeUntil(ref index, lastIndex, predicates))
        {
            Index = index;
            return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Goto(char character, StringComparison comparisonType = StringComparison.Ordinal)
    {
        var (str, index, lastIndex) = this;

        if (!str.GotoAndConsume(ref index, lastIndex, character, comparisonType))
            return false;

        Index = index;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Goto(string subString, StringComparison comparisonType = StringComparison.Ordinal)
    {
        var (str, index, lastIndex) = this;

        if (!str.Goto(ref index, lastIndex, subString, comparisonType))
            return false;

        Index = index;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Goto(string subString, int subIndex, int length, StringComparison comparisonType = StringComparison.Ordinal)
    {
        var (str, index, lastIndex) = this;

        if (!str.Goto(ref index, lastIndex, subString, subIndex, length, comparisonType))
            return false;

        Index = index;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Goto(StringView sub, StringComparison comparisonType = StringComparison.Ordinal)
    {
        var (str, index, lastIndex) = this;
        var (subString, subIndex, subLst) = sub;
        var length = subLst - subIndex;

        if (!str.Goto(ref index, lastIndex, subString, subIndex, length, comparisonType))
            return false;

        Index = index;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool GotoAndConsume(char character, StringComparison comparisonType = StringComparison.Ordinal)
    {
        var (str, index, lastIndex) = this;

        if (!str.GotoAndConsume(ref index, lastIndex, character, comparisonType))
            return false;

        Index = index;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool GotoAndConsume(string subString, StringComparison comparisonType = StringComparison.Ordinal)
    {
        var (str, index, lastIndex) = this;

        if (!str.GotoAndConsume(ref index, lastIndex, subString, comparisonType))
            return false;

        Index = index;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool GotoAndConsume(string subString, int subIndex, int length, StringComparison comparisonType = StringComparison.Ordinal)
    {
        var (str, index, lastIndex) = this;

        if (!str.GotoAndConsume(ref index, lastIndex, subString, subIndex, length, comparisonType))
            return false;

        Index = index;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool GotoAndConsume(StringView sub, StringComparison comparisonType = StringComparison.Ordinal)
    {
        var (str, index, lastIndex) = this;
        var (subString, subIndex, subLst) = sub;
        var length = subLst - subIndex;

        if (!str.GotoAndConsume(ref index, lastIndex, subString, subIndex, length, comparisonType))
            return false;

        Index = index;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool GotoAndConsume(Func<char, bool> predicate)
    {
        var (str, index, lastIndex) = this;

        if (!str.GotoAndConsume(ref index, lastIndex, predicate))
            return false;

        Index = index;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool GotoAndConsume(params Func<char, bool>[] predicates)
    {
        var (str, index, lastIndex) = this;

        if (!str.GotoAndConsume(ref index, lastIndex, predicates))
            return false;

        Index = index;
        return true;
    }
}