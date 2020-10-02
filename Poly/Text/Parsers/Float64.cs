using System;

namespace Poly
{
    public static partial class StringFloat64Parser
    {
        public static bool TryParse(this string text, out double value)
            => TryParse(text, 0, text?.Length ?? -1, out value);

        public static bool TryParse(this string text, int index, int lastIndex, out double value)
            => TryParse(text, ref index, lastIndex, out value)
            && index == lastIndex;

        public static bool TryParse(this string text, ref int index, int lastIndex, out double value) {            
            if (!Iteration.BoundsCheck(text, index, lastIndex)) {
                value = double.NaN;
                return false;
            }


            var offset = index;

            if (text[offset] == '-')
            {
                offset++;
            }

            while (offset < lastIndex)
            {
                if ((text[offset] ^ '0') > 9)
                    break;

                offset++;
            }

            if (offset < lastIndex && text[offset] == '.')
            {
                offset++;

                while (offset < lastIndex)
                {
                    if ((text[offset] ^ '0') > 9)
                        break;

                    offset++;
                }
            }

            if (offset < lastIndex && (text[offset] == 'e' || text[offset] == 'E'))
            {
                offset++;

                if (text[offset] == '-')
                    offset++;
                else
                if (text[offset] == '+')
                    offset++;

                while (offset < lastIndex)
                {
                    if ((text[offset] ^ '0') > 9)
                        break;

                    offset++;
                }
            }

            if (double.TryParse(text.AsSpan(index, offset - index), out value)) {
                index = offset;
                return true;
            }

            return false;
        }

        /* Future work, must improve accuracy of mantissa

        public static bool TryParse(this string text, ref int index, int lastIndex, out double value)
        {
            if (!Iteration.BoundsCheck(text, index, lastIndex))
                goto failure;

            char character;

            var fraction = 0L;
            var fraction_sign = 1L;
            var fraction_decimal_digits = 0;

            var exponent = 0;
            var exponent_sign = 1;

            var offset = index;

            try
            {
                if (text[offset] == '-')
                {
                    fraction_sign = -1L;
                    offset++;
                }

                while (offset < lastIndex)
                {
                    character = text[offset];

                    if ((character ^ '0') > 9)
                        break;

                    fraction = checked(fraction * 10) + (int)(character - '0');
                    offset++;
                }

                if (offset < lastIndex && text[offset] == '.')
                {
                    offset++;

                    while (offset < lastIndex)
                    {
                        character = text[offset];

                        if ((character ^ '0') > 9)
                            break;

                        fraction = checked(fraction * 10) + (int)(character - '0');
                        fraction_decimal_digits++;
                        offset++;
                    }
                }

                fraction *= fraction_sign;

                if (offset < lastIndex && (text[offset] == 'e' || text[offset] == 'E'))
                {
                    offset++;

                    if (offset >= lastIndex)
                        goto failure;

                    if (text[offset] == '-')
                    {
                        exponent_sign = -1;
                        offset++;
                    }
                    else
                    if (text[offset] == '+')
                        offset++;

                    while (offset < lastIndex)
                    {
                        character = text[offset];

                        if ((character ^ '0') > 9)
                            break;

                        exponent = checked(exponent * 10) + (int)(character - '0');
                        offset++;
                    }

                    if (exponent > 308)
                        goto failure;

                    exponent *= exponent_sign;
                }

                value = validPowersOf10[308 - fraction_decimal_digits + exponent] * fraction;
                index = offset;
                return true;
            }
            catch { }

        failure:
            value = double.NaN;
            return false;
        }

        */
    }
}