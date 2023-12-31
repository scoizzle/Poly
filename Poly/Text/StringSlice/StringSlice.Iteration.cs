namespace Poly;

public partial struct StringSlice {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Consume(int numberOfCharacters = 1) {
        if (!BoundsCheck(numberOfCharacters))
            return false;

        Begin += numberOfCharacters;
        return true;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Consume(out char character) {
        if (!BoundsCheck(1)) {
            character = default;
            return false;
        }

        character = String[Begin++];
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Consume(char character) {
        if (!IsAt(character))
            return false;

        Begin++;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Consume(string text, StringComparison comparison = StringComparison.Ordinal) {
        if (!IsAt(text, comparison))
            return false;

        Begin += text.Length;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Consume(string text, int index, int length, StringComparison comparison = StringComparison.Ordinal) {
        if (!IsAt(text, index, length, comparison))
            return false;

        Begin += length;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Consume(in StringSlice sub, StringComparison comparison = StringComparison.Ordinal) {
        if (!IsAt(sub, comparison))
            return false;

        Begin += sub.Length;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Consume(Func<char, bool> predicate) {
        if (!BoundsCheck())
            return false;

        var offset = Begin;

        while (offset < End) {
            if (!predicate(String[offset]))
                break;

            offset++;
        }

        if (offset == Begin)
            return false;

        Begin = offset;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Consume(params Func<char, bool>[] predicates) {
        Guard.IsNotNull(predicates);
        
        if (!BoundsCheck())
            return false;

        var offset = Begin;

        while (offset < End) {
            var character = String[offset];
            var precondition = false;

            for (var i = 0; i < predicates.Length && !precondition; i++) {
                precondition = predicates[i](character);
            }

            if (!precondition)
                break;

            offset++;
        }

        if (offset == Begin)
            return false;

        Begin = offset;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ConsumeUntil(Func<char, bool> predicate) {
        if (!BoundsCheck())
            return false;

        var offset = Begin;

        while (offset < End) {
            if (predicate(String[offset]))
                break;

            offset++;
        }

        if (offset == Begin)
            return false;

        Begin = offset;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ConsumeUntil(params Func<char, bool>[] predicates) {
        Guard.IsNotNull(predicates);

        if (!BoundsCheck())
            return false;

        var offset = Begin;

        while (offset < End) {
            var character = String[offset];
            var precondition = false;

            for (var i = 0; i < predicates.Length && !precondition; i++) {
                precondition = predicates[i](character);
            }

            if (precondition)
                break;

            offset++;
        }

        if (offset == Begin)
            return false;

        Begin = offset;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Goto(char character) {
        if (!BoundsCheck())
            return false;

        var location = IndexOf(character);

        if (location == -1)
            return false;

        Begin = location;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Goto(string subString) {
        if (!BoundsCheck())
            return false;

        var location = IndexOf(subString);

        if (location == -1)
            return false;

        Begin = location;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Goto(string subString, int subIndex, int length) {
        if (!BoundsCheck(subString, subIndex, length))
            return false;

        var location = IndexOf(subString, subIndex, length);

        if (location == -1)
            return false;

        Begin = location;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool GotoAndConsume(char character) {
        if (!BoundsCheck())
            return false;

        var location = IndexOf(character);

        if (location == -1)
            return false;

        Begin = location + 1;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool GotoAndConsume(string subString) {
        if (!BoundsCheck(subString))
            return false;

        var location = IndexOf(subString);

        if (location == -1)
            return false;

        Begin = location + subString.Length;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool GotoAndConsume(string subString, int subIndex, int length) {
        if (!BoundsCheck(subString, subIndex, length))
            return false;

        var location = IndexOf(subString, subIndex, length);

        if (location == -1)
            return false;

        Begin = location + length;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool GotoAndConsume(Func<char, bool> predicate) {
        if (!BoundsCheck())
            return false;

        var offset = Begin;

        while (offset < End) {
            if (predicate(String[offset]))
                break;

            offset++;
        }

        if (offset == End)
            return false;

        while (++offset < End) {
            if (!predicate(String[offset]))
                break;
        }

        Begin = offset;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool GotoAndConsume(params Func<char, bool>[] predicates) {
        Guard.IsNotNull(predicates);
        
        if (!BoundsCheck())
            return false;

        var offset = Begin;

        while (offset < End) {
            var character = String[offset];
            var precondition = false;

            for (var i = 0; i < predicates.Length && !precondition; i++) {
                precondition = predicates[i](character);
            }

            if (precondition)
                break;

            offset++;
        }

        if (offset == End)
            return false;

        while (++offset < End) {
            var character = String[offset];
            var precondition = false;

            for (var i = 0; i < predicates.Length && !precondition; i++) {
                precondition = predicates[i](character);
            }

            if (!precondition)
                break;
        }

        Begin = offset;
        return true;
    }
}