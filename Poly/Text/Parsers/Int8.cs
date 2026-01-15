namespace Poly {
    public static class StringInt8Parser {
        public static bool TryParse(this string text, out sbyte value)
            => TryParse(text, 0, text?.Length ?? -1, out value);

        public static bool TryParse(this string text, int index, int lastIndex, out sbyte value)
            => TryParse(text, ref index, lastIndex, out value) && index == lastIndex;

        public static bool TryParse(this string text, ref int index, int lastIndex, out sbyte value)
        {
            if (!StringIteration.BoundsCheck(text, index, lastIndex))
                goto failure;

            int offset = index;
            sbyte result = 0;

            bool negative = text[offset] == '-';

            if (negative)
                offset++;

            try {
                while (offset < lastIndex) {
                    var character = text[offset];

                    if (character < '0' || character > '9')
                        break;

                    result = (sbyte)(checked(result * 10) + character - '0');
                    offset++;
                }

                if (offset < lastIndex && text[offset] == '.')
                    goto failure;

                index = offset;
                value = negative ? (sbyte)(-result) : result;
                return true;
            }
            catch { }

        failure:
            value = default;
            return false;
        }

        public static bool TryParse(this string This, out byte value) =>
            TryParse(This, 0, This?.Length ?? -1, out value);

        public static bool TryParse(this string text, int index, int lastIndex, out byte value) =>
            TryParse(text, ref index, lastIndex, out value) && index == lastIndex;

        public static bool TryParse(this string text, ref int index, int lastIndex, out byte value)
        {
            if (!StringIteration.BoundsCheck(text, index, lastIndex))
                goto failure;

            int offset = index;
            byte result = 0;

            try {
                while (offset < lastIndex) {
                    var character = text[offset];

                    if (character < '0' || character > '9')
                        break;

                    result = (byte)(checked(result * 10) + character - '0');
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