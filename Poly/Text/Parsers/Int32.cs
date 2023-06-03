namespace Poly
{
    public static class StringInt32Parser
    {
        public static bool TryParse(this string text, out int value)
            => TryParse(text, 0, text?.Length ?? -1, out value);

        public static bool TryParse(this string text, int index, int lastIndex, out int value)
            => TryParse(text, ref index, lastIndex, out value) && index == lastIndex;
            
        public static bool TryParse(this string text, ref int index, int lastIndex, out int value) {
            if (!Iteration.BoundsCheck(text, index, lastIndex)) {
                value = default;
                return false;
            }

            int offset = index;
            int result = 0;

            bool negative = text[offset] == '-';

            if (negative)
                offset++;

            while (offset < lastIndex) {
                var digit = text[offset] - '0';

                if (digit < 0 || digit > 9)
                    break;

                var next = result * 10 + digit;

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
            value = negative ? -result : result;
            return true;
        }
        
        public static bool TryParse(this string This, out uint value) =>
            TryParse(This, 0, This?.Length ?? -1, out value);

        public static bool TryParse(this string text, int index, int lastIndex, out uint value) =>
            TryParse(text, ref index, lastIndex, out value) && index == lastIndex;

        public static bool TryParse(this string text, ref int index, int lastIndex, out uint value) {
            if (!Iteration.BoundsCheck(text, index, lastIndex)) {
                value = default;
                return false;
            }

            int offset = index;
            uint result = 0;

            while (offset < lastIndex) {
                var digit = text[offset] - '0';

                if (digit < 0 || digit > 9)
                    break;

                var next = (uint)(result * 10 + digit);

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