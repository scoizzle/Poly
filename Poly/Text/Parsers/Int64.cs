namespace Poly {
    public static class StringInt64Parser {
        public static bool TryParse(this string text, out long value)
            => TryParse(text, 0, text?.Length ?? -1, out value);

        public static bool TryParse(this string text, int index, int lastIndex, out long value)
            => TryParse(text, ref index, lastIndex, out value) && index == lastIndex;

        public static bool TryParse(this string text, ref int index, int lastIndex, out long value)
        {
            if (!StringIteration.BoundsCheck(text, index, lastIndex)) {
                value = default;
                return false;
            }

            int offset = index;
            long result = 0;

            bool negative = text[offset] == '-';

            if (negative)
                offset++;

            while (offset < lastIndex) {
                var digit = text[offset] - '0';

                if (digit < 0 || digit > 9)
                    break;

                result *= 10;

                if (result < 0) {
                    value = default;
                    return false;
                }

                result += digit;
                offset++;
            }

            if (offset < lastIndex && text[offset] == '.') {
                value = default;
                return false;
            }

            index = offset;
            value = negative ? -result : result;
            return true;
        }

        public static bool TryParse(this string This, out ulong value) =>
            TryParse(This, 0, This?.Length ?? -1, out value);

        public static bool TryParse(this string text, int index, int lastIndex, out ulong value) =>
            TryParse(text, ref index, lastIndex, out value) && index == lastIndex;

        public static bool TryParse(this string text, ref int index, int lastIndex, out ulong value)
        {
            if (!StringIteration.BoundsCheck(text, index, lastIndex)) {
                value = default;
                return false;
            }

            int offset = index;
            ulong result = 0;

            while (offset < lastIndex) {
                var digit = text[offset] - '0';

                if (digit < 0 || digit > 9)
                    break;

                var next = result * 10 + (ulong)(digit);

                if (next < result) {
                    value = default;
                    return false;
                }

                result = next;
                offset++;
            }

            if (offset < lastIndex && text[offset] == '.') {
                value = default;
                return false;
            }

            index = offset;
            value = result;
            return true;
        }
    }
}