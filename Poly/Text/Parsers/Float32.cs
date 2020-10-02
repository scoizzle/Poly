using System;

namespace Poly
{
    public static class StringFloat32Parser
    {
        public static bool TryParse(this string text, out float value)
            => TryParse(text, 0, text?.Length ?? -1, out value);

        public static bool TryParse(this string text, int index, int lastIndex, out float value)
            => TryParse(text, ref index, lastIndex, out value)
            && index == lastIndex;
        
        public static bool TryParse(this string text, ref int index, int lastIndex, out float value) {            
            if (!Iteration.BoundsCheck(text, index, lastIndex)) {
                value = float.NaN;
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

            if (float.TryParse(text.AsSpan(index, offset - index), out value)) {
                index = offset;
                return true;
            }

            return false;
        }
    }
}