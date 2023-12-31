using System;

namespace Poly
{
    public static class StringViewExtraction
    {
        public static bool ExtractBetween(ref this StringView view, char open, char close, out StringView section, bool includeBraces = false)
        {
            var location = Searching.FindMatchingBracket(view.String, view.Index, view.LastIndex, open, close);

            if (location == -1)
            {
                section = default;
                return false; 
            }

            section = includeBraces ?
                new StringView(view.String, view.Index, location + 1) :
                new StringView(view.String, view.Index + 1, location);

            view.Index = location + 1;
            return true;
        }

        public static bool ExtractStringLiteral(ref this StringView view, out StringView section, bool includeQuotes = false)
            => ExtractBetween(ref view, '"', '"', out section, includeQuotes);

        public static ReadOnlySpan<char> ExtractAsSpan(ref this StringView view, int length) {
            if (!Iteration.BoundsCheck(view.String, view.Index, view.LastIndex, length)) {
                return ReadOnlySpan<char>.Empty;
            }
            
            try {
                return view.AsSpan(length: length);
            }
            finally {
                view.Consume(length);
            }
        }

        public static ReadOnlySpan<char> ExtractWhileAsSpan(ref this StringView view, Func<char, bool> predicate) {
            var index = view.Index;

            while (Iteration.Consume(view.String, ref index, view.LastIndex, out char chr) && predicate(chr));

            try {
                return view.AsSpan(length: index - view.Index);
            }
            catch { 
                return ReadOnlySpan<char>.Empty;
            }
            finally {
                view.Index = index;
            }
        }

        public static ReadOnlySpan<char> ExtractUntilCharOrEndAsSpan(ref this StringView view, ReadOnlySpan<char> characters, bool inclusive = true) {
            var indexOfCharacter = view.IndexOfAny(characters);

            if (indexOfCharacter == -1)
                indexOfCharacter = view.LastIndex;

            // Include character in returned span
            if (inclusive)
                indexOfCharacter ++;

            try {
                return view.AsSpan(length: indexOfCharacter - view.Index);
            }
            catch { 
                return ReadOnlySpan<char>.Empty;
            }
            finally {
                view.Index = indexOfCharacter;
            }
        }

        public static ReadOnlySpan<char> ExtractStringLiteralAsSpan(ref this StringView view) {
            
            var location = Searching.FindMatchingBracket(view.String, view.Index, view.LastIndex, '"', '"');

            if (location == -1)
            {
                return ReadOnlySpan<char>.Empty;
            }

            try {
                return view.AsSpan(view.Index + 1, location - view.Index);
            }
            finally {
                view.Index = location + 1;
            }
        }

        public static bool Extract(ref this StringView view, out sbyte value)
            => StringInt8Parser.TryParse(view.String, ref view.Index, view.LastIndex, out value);

        public static bool Extract(ref this StringView view, out byte value)
            => StringInt8Parser.TryParse(view.String, ref view.Index, view.LastIndex, out value);

        public static bool Extract(ref this StringView view, out short value)
            => StringInt16Parser.TryParse(view.String, ref view.Index, view.LastIndex, out value);

        public static bool Extract(ref this StringView view, out ushort value)
            => StringInt16Parser.TryParse(view.String, ref view.Index, view.LastIndex, out value);

        public static bool Extract(ref this StringView view, out int value)
            => StringInt32Parser.TryParse(view.String, ref view.Index, view.LastIndex, out value);

        public static bool Extract(ref this StringView view, out uint value)
            => StringInt32Parser.TryParse(view.String, ref view.Index, view.LastIndex, out value);

        public static bool Extract(ref this StringView view, out long value)
            => StringInt64Parser.TryParse(view.String, ref view.Index, view.LastIndex, out value);

        public static bool Extract(ref this StringView view, out ulong value)
            => StringInt64Parser.TryParse(view.String, ref view.Index, view.LastIndex, out value);

        public static bool Extract(ref this StringView view, out float value)
            => StringFloat32Parser.TryParse(view.String, ref view.Index, view.LastIndex, out value);

        public static bool Extract(ref this StringView view, out double value)
            => StringFloat64Parser.TryParse(view.String, ref view.Index, view.LastIndex, out value);

        public static bool Extract(ref this StringView view, out decimal value)
            => StringFloat128Parser.TryParse(view.String, ref view.Index, view.LastIndex, out value);

        public static bool Extract(ref this StringView view, out bool value, StringComparison stringComparison = StringComparison.Ordinal)
        {
            if (StringViewConsumption.Consume(ref view, "false", stringComparison))
            {
                value = false;
                return true;
            }

            if (StringViewConsumption.Consume(ref view, "true", stringComparison))
            {
                value = true;
                return true;
            }

            value = default;
            return false;
        }
    }
}