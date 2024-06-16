namespace Poly
{
    public static class StringInt16Parser
    {
        public static bool TryParse(this string text, out short value) =>
            TryParse(text, 0, text?.Length ?? -1, out value);

        public static bool TryParse(this string text, int index, int lastIndex, out short value) =>
            TryParse(text, ref index, lastIndex, out value) && index == lastIndex;

        public static bool TryParse(this string text, ref int index, int lastIndex, out short value)
        {
            if (!StringIteration.BoundsCheck(text, index, lastIndex))
                goto failure;

            int offset = index;
            short result = 0;

            bool negative = text[offset] == '-';

            if (negative)
                offset++;

            try
            {
                while (offset < lastIndex)
                {
                    var character = text[offset];

                    if (character < '0' || character > '9')
                        break;

                    result = (short)(checked(result * 10) + character - '0');
                    offset++;
                }

                if (offset < lastIndex && text[offset] == '.')
                    goto failure;

                index = offset;
                value = negative ? (short)(-result) : result;
                return true;
            }
            catch { }

        failure:
            value = default;
            return false;
        }

        public static bool TryParse(this string This, out ushort value) =>
            TryParse(This, 0, This?.Length ?? -1, out value);

        public static bool TryParse(this string text, int index, int lastIndex, out ushort value) =>
            TryParse(text, ref index, lastIndex, out value) && index == lastIndex;

        public static bool TryParse(this string text, ref int index, int lastIndex, out ushort value)
        {
            if (!StringIteration.BoundsCheck(text, index, lastIndex))
                goto failure;

            int offset = index;
            ushort result = 0;

            try
            {
                while (offset < lastIndex)
                {
                    var character = text[offset];

                    if (character < '0' || character > '9')
                        break;

                    result = (ushort)(checked(result * 10) + character - '0');
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