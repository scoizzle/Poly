using Poly;
using Poly.Data;
using System.IO;
using System.Text;

namespace System {
    public static class StringInt8Parser {
        const uint MaxStringLength = 3; // byte.MaxValue.ToString().Length;

        public static bool TryParse(this string text, out sbyte value) =>
            TryParse(text, 0, text?.Length ?? -1, out value);

        public static bool TryParse(this string text, int index, int last_index, out sbyte value) {
            int n, x;
            int digit;
            char character;

            if (!text.BoundsCheck(index, last_index))
                goto format_error;

            n = default;

            if (text.Consume(ref index, '-')) {
                if (MaxStringLength < last_index - index)
                    goto format_error;

                if (!text.TraverseForward(ref index, last_index, out character))
                    goto format_error;

                if (!character.TryParseNumber(out digit))
                    goto format_error;

                n = digit * -1;

                while (text.TraverseForward(ref index, last_index, out character)) {
                    if (!character.TryParseNumber(out digit))
                        goto format_error;

                    x = n * 10;

                    if (x > n)
                        goto format_error;

                    n = x - digit;

                    if (n > x)
                        goto format_error;
                }
            }
            else {
                if (MaxStringLength < last_index - index)
                    goto format_error;

                while (text.TraverseForward(ref index, last_index, out character)) {
                    if (!character.TryParseNumber(out digit))
                        goto format_error;

                    x = n * 10;

                    if (x < n)
                        goto format_error;

                    n = x + digit;

                    if (n < x)
                        goto format_error;
                }
            }

            if (n > sbyte.MaxValue || n < sbyte.MinValue)
                goto format_error;

            value = (sbyte)(n);
            return true;

        format_error:
            value = default;
            return false;
        }

        public static bool TryParse(this string This, out byte value) =>
            TryParse(This, 0, This?.Length ?? -1, out value);

        public static bool TryParse(this string text, int index, int last_index, out byte value) {
            uint n, x;
            int digit;
            char character;

            if (MaxStringLength < last_index - index)
                goto format_error;

            if (!text.BoundsCheck(index, last_index))
                goto format_error;

            n = default;

            while (text.TraverseForward(ref index, last_index, out character)) {
                if (!character.TryParseNumber(out digit))
                    goto format_error;

                x = n * 10;

                if (x < n)
                    goto format_error;

                n = x + (uint)(digit);

                if (n < x)
                    goto format_error;
            }

            if (n > byte.MaxValue)
                goto format_error;

            value = (byte)(n);
            return true;

        format_error:
            value = default;
            return false;
        }
    }
}