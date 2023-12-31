namespace Poly;

public partial struct StringSlice
{
    public readonly StringSlice? this[char chr, bool includeChr = false] 
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get {
            var index = Begin;

            do
            {
                index = IndexOf(chr);

                if (index == -1)
                    break;

                if (this[index - 1] == '\\') {
                    index++;
                    continue;
                }

                if (index == Begin)
                    return this;

                if (includeChr)
                    return this[Begin, index + 1];

                return this[Begin + 1, index];
            }
            while (index < End);

            return default;
        }
    }

    public readonly StringSlice? this[char[] charsToSearchFor, bool includeChr = false] 
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get {
            Guard.IsNotNull(charsToSearchFor);
            
            if (!BoundsCheck())
                return default;

            for (var index = Begin; index < End; index++)
            {
                var character = String[index];

                if (character == '\\')
                    continue;
                
                for (var i = 0; i < charsToSearchFor.Length; i++) {
                    if (character == charsToSearchFor[i]) {
                        if (includeChr)
                            index++;
                            
                        return this[Begin, index];
                    }
                }
            }

            return default;
        }
    }

    public readonly StringSlice? this[char open, char close, bool includeOpenClose = false] 
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get {            
            if (!IsAt(open))
                return default;

            var index = Begin + 1;
            var count = 1;

            while (index < End)
            {
                var character = String[index];
                
                if (character == close && --count == 0) {
                    if (includeOpenClose)
                        return this[Begin, index + 1];

                    return this[Begin + 1, index];
                }
                else
                if (character == open) {
                    count++;
                }
                else
                if (character == '\\') {
                    index++;
                }

                index++;
            }

            return default;
        }
    }

    public readonly StringSlice? this[string open, string close, bool includeOpenClose = false] 
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get {
            Guard.IsNotNull(open);
            Guard.IsNotNull(close);

            if (!IsAt(open))
                return default;

            var index = String.IndexOf(close, Begin + open.Length);

            if (index == -1) 
                return default;

            if (includeOpenClose)
                return this[Begin, index + close.Length];

            return this[Begin + open.Length, index];
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool ExtractBetween(
        char open, 
        char close, 
        out StringSlice section, 
        bool includeBraces = false)
    {
        var slice = this[open, close, includeBraces];

        if (!slice.HasValue) {
            section = default;
            return false;
        }

        section = slice.Value;
        return true;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool ExtractStringLiteral(
        out StringSlice section, 
        bool includeQuotes = false) 
    {
        var slice = this['"', '"', includeQuotes];

        if (!slice.HasValue) {
            section = default;
            return false;
        }

        section = slice.Value;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool TryParse(out bool value) 
    {
        if (!BoundsCheck()) {
            value = default;
            return false;
        }

        var span = String.AsSpan(Begin, Length);

        return bool.TryParse(span, out value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool TryParse<T>(out T? value, IFormatProvider? formatProvider = default) where T : ISpanParsable<T> 
    {
        if (!BoundsCheck()) {
            value = default;
            return false;
        }

        var span = String.AsSpan(Begin, Length);

        return T.TryParse(span, formatProvider, out value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool TryParseString<T>(out T? value, IFormatProvider? formatProvider = default) where T : IParsable<T> 
    {
        if (!BoundsCheck()) {
            value = default;
            return false;
        }

        var sub = String.Substring(Begin, Length);

        return T.TryParse(sub, formatProvider, out value);
    }
}