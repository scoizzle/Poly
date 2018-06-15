using Poly;
using Poly.Data;
using System.IO;
using System.Text;

namespace System {
    public static class StringInt16Parser {
        const uint MaxStringLength = 5; // short.MaxValue.ToString().Length;

        public static bool TryParse(this string text, out short value) =>
            TryParse(text, 0, text?.Length ?? -1, out value);

        public static bool TryParse(this string text, int index, int last_index, out short value) {
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

            if (n > short.MaxValue || n < short.MinValue)
                goto format_error;

            value = (short)(n);
            return true;

        format_error:
            value = default;
            return false;
        }

        public static bool TryParse(this string This, out ushort value) =>
            TryParse(This, 0, This?.Length ?? -1, out value);

        public static bool TryParse(this string text, int index, int last_index, out ushort value) {
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

            if (n > ushort.MaxValue)
                goto format_error;

            value = (ushort)(n);
            return true;

        format_error:
            value = default;
            return false;
        }
    }
}