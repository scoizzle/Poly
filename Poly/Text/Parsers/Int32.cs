namespace Poly
{
    public static class StringInt32Parser
    {
        public static bool TryParse(this string text, out int value)
            => TryParse(text, 0, text?.Length ?? -1, out value);

        public static bool TryParse(this string text, int index, int lastIndex, out int value)
            => TryParse(text, ref index, lastIndex, out value) && index == lastIndex;
            
        public static bool TryParse(this string text, ref int index, int lastIndex, out int value) {
            if (!Iteration.BoundsCheck(text, index, lastIndex))
                goto failure;

            int offset = index;
            int result = 0;

            bool negative = text[offset] == '-';

            if (negative)
                offset++;

            try {
                while (offset < lastIndex) {
                    var digit = text[offset] - '0';

                    if (digit < 0 || digit > 9)
                        break;

                    result = checked(result * 10) + digit;
                    offset++;
                }

                if (offset < lastIndex && text[offset] == '.')
                    goto failure;

                index = offset;
                value = negative ? -result : result;
                return true;
            }
            catch { }

        failure:
            value = default;
            return false;
        }

        public static bool TryParse(this string This, out uint value) =>
            TryParse(This, 0, This?.Length ?? -1, out value);

        public static bool TryParse(this string text, int index, int lastIndex, out uint value) =>
            TryParse(text, ref index, lastIndex, out value) && index == lastIndex;

        public static bool TryParse(this string text, ref int index, int lastIndex, out uint value) {
            if (!Iteration.BoundsCheck(text, index, lastIndex))
                goto failure;

            int offset = index;
            uint result = 0;

            try {
                while (offset < lastIndex) {
                    var character = text[offset];

                    if (character < '0' || character > '9')
                        break;

                    result = checked(result * 10) + (uint)(character - '0');
                    offset++;
                }

                if (offset < lastIndex && text[offset] == '.')
                    goto failure;

                index = offset;
                value = result;
                return true;
            }
            catch { }

        failure:
            value = default;
            return false;
        }
    }
}