using System;
using System.Collections.Generic;

namespace Poly
{
    public static class Searching
    {
        public static int IndexOf(this string This, int index, int lastIndex, char character)
        {
            if (!Iteration.BoundsCheck(This, index, lastIndex))
                return -1;

            return This.IndexOf(character, index, lastIndex - index);
        }

        public static int IndexOf(this string This, int index, int lastIndex, string subString, StringComparison comparison = StringComparison.Ordinal)
        {
            if (!Iteration.BoundsCheck(This, index, lastIndex, subString))
                return -1;

            for (var last_possible = lastIndex - subString.Length; index <= last_possible; index++)
            {
                if (This[index] != subString[0])
                    continue;

                if (string.Compare(This, index, subString, 0, subString.Length, comparison) == 0)
                    return index;
            }

            return -1;
        }

        public static int IndexOf(this string This, int index, int lastIndex, string subString, int subIndex, int length, StringComparison comparison = StringComparison.Ordinal)
        {
            if (!Iteration.BoundsCheck(This, index, lastIndex, subString, subIndex, length))
                return -1;

            for (var last_possible = lastIndex - length; index <= last_possible; index++)
            {
                if (This[index] != subString[subIndex])
                    continue;

                if (string.Compare(This, index, subString, subIndex, length, comparison) == 0)
                    return index;
            }

            return -1;
        }

        public static IEnumerable<int> FindAll(this string This, int index, int lastIndex, char character)
        {
            if (Iteration.BoundsCheck(This, index, lastIndex))
            {
                do
                {
                    if (This[index] == character)
                        yield return index++;
                }
                while (index++ < lastIndex);
            }
        }

        public static IEnumerable<int> FindAll(this string This, int index, int lastIndex, string subString, StringComparison comparison = StringComparison.Ordinal)
        {
            if (!Iteration.BoundsCheck(This, index, lastIndex, subString))
                yield break;

            for (var last_possible = lastIndex - subString.Length; index <= last_possible; index++)
            {
                if (This[index] != subString[0])
                    continue;

                if (string.Compare(This, index, subString, 0, subString.Length, comparison) == 0)
                {
                    yield return index;
                    index += subString.Length;
                }
            }
        }

        public static IEnumerable<int> FindAll(this string This, int index, int lastIndex, string subString, int subIndex, int length, StringComparison comparison = StringComparison.Ordinal)
        {
            if (!Iteration.BoundsCheck(This, index, lastIndex, subString, subIndex, length))
                yield break;

            for (var last_possible = lastIndex - length; index <= last_possible; index++)
            {
                if (This[index] != subString[subIndex])
                    continue;

                if (string.Compare(This, index, subString, subIndex, length, comparison) == 0)
                {
                    yield return index;
                    index += length;
                }
            }
        }

        public static int FindMatchingBracket(
            this string This,
            int index,
            int lastIndex,
            char open,
            char close
            )
        {
            if (!Iteration.Consume(This, ref index, lastIndex, open))
                return -1;

            var count = 1;

            while (index < lastIndex)
            {
                var character = This[index];

                if (character == '\\')
                    index++;
                else
                if (character == close && --count == 0)
                    return index;
                else
                if (character == open)
                    count++;

                index++;
            }

            return -1;
        }
    }
}