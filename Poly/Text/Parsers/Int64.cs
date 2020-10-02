namespace Poly {
    public static class StringInt64Parser {
        public static bool TryParse(this string text, out long value)
            => TryParse(text, 0, text?.Length ?? -1, out value);

        public static bool TryParse(this string text, int index, int lastIndex, out long value)
            => TryParse(text, ref index, lastIndex, out value) && index == lastIndex;

        public static bool TryParse(this string text, ref int index, int lastIndex, out long value) {
            if (!Iteration.BoundsCheck(text, index, lastIndex))
                goto failure;

            int offset = index;
            long result = 0;

            bool negative = text[offset] == '-';

            if (negative)
                offset++;

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
                value = negative ? -result : result;
                return true;
            }
            catch { }

        failure:
            value = default;
            return false;
        }

        public static bool TryParse(this string This, out ulong value) =>
            TryParse(This, 0, This?.Length ?? -1, out value);

        public static bool TryParse(this string text, int index, int lastIndex, out ulong value) =>
            TryParse(text, ref index, lastIndex, out value) && index == lastIndex;

        public static bool TryParse(this string text, ref int index, int lastIndex, out ulong value) {
            if (!Iteration.BoundsCheck(text, index, lastIndex))
                goto failure;

            int offset = index;
            ulong result = 0;

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