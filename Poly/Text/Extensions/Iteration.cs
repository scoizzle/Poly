using System;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Poly {
    public static class Iteration {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool BoundsCheck(this string This, int index)
            => This != null
            && index >= 0 
            && index < This.Length;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool BoundsCheck(this string This, int index, int lastIndex)
            => This != null
            && index >= 0
            && index < lastIndex
            && lastIndex <= This.Length;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool BoundsCheck(this string This, int index, int lastIndex, int length)
            => This != null
            && index >= 0 
            && index + length <= lastIndex 
            && lastIndex <= This.Length;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool BoundsCheck(this string This, int index, int lastIndex, string subString)
            => This != null
            && subString != null
            && index >= 0 
            && index + subString.Length <= lastIndex
            && lastIndex <= This.Length;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool BoundsCheck(this string This, int index, int lastIndex, string subString, int subIndex, int length)
            => This != null
            && index >= 0 
            && index + length <= lastIndex
            && lastIndex <= This.Length
            && subString != null
            && subIndex >= 0
            && subIndex + length <= subString.Length;

        public static bool IsAt(this string This, int index, char character)
            => BoundsCheck(This, index)
            && This[index] == character;

        public static bool IsAt(this string This, int index, int lastIndex, char character)
            => BoundsCheck(This, index, lastIndex)
            && This[index] == character;

        public static bool IsAt(this string This, int index, int lastIndex, string subString, StringComparison comparison = StringComparison.Ordinal)
            => BoundsCheck(This, index, lastIndex, subString)
            && string.Compare(This, index, subString, 0, subString.Length, comparison) == 0;

        public static bool IsAt(this string This, int index, int lastIndex, string subString, int subIndex, int length, StringComparison comparison = StringComparison.Ordinal)
            => BoundsCheck(This, index, lastIndex, subString, subIndex, length)
            && string.Compare(This, index, subString, subIndex, length, comparison) == 0;

        public static bool IsAt(this string This, int index, int lastIndex, Func<char, bool> predicate)
            => BoundsCheck(This, index, lastIndex) 
            && predicate(This[index]);

        public static bool IsAt(this string This, int index, int lastIndex, params Func<char, bool>[] predicates)
            => BoundsCheck(This, index, lastIndex)
            && predicates.Any(f => f(This[index]));

        public static bool IsNotAt(this string This, int index, int lastIndex, Func<char, bool> predicate)
            => BoundsCheck(This, index, lastIndex) 
            && !predicate(This[index]);

        public static bool IsNotAt(this string This, int index, int lastIndex, params Func<char, bool>[] predicates)
            => BoundsCheck(This, index, lastIndex)
            && !predicates.Any(f => f(This[index]));

        public static bool Equals(this string This, int index, int lastIndex, string subString, StringComparison comparison = StringComparison.Ordinal)
            => BoundsCheck(This, index, lastIndex, subString)
            && lastIndex - index == subString.Length
            && string.Compare(This, index, subString, 0, subString.Length, comparison) == 0;

        public static bool Equals(this string This, int index, int lastIndex, string subString, int subIndex, int length, StringComparison comparison = StringComparison.Ordinal)
            => BoundsCheck(This, index, lastIndex, subString, subIndex, length)
            && lastIndex - index == length
            && string.Compare(This, index, subString, subIndex, length, comparison) == 0;

        public static bool Consume(this string This, ref int index, int lastIndex, int n) {
            if (BoundsCheck(This, index, lastIndex, n)) {
                index += n;
                return true;
            }

            return false;
        }

        public static bool Consume(this string This, ref int index, char character)
        {
            if (IsAt(This, index, character))
            {
                index++;
                return true;
            }

            return false;
        }

        public static bool Consume(this string This, int index, char character, out int position)
        {
            if (IsAt(This, index, character))
            {
                position = index + 1;
                return true;
            }

            position = -1;
            return false;
        }

        public static bool Consume(this string This, ref int index, int lastIndex, char character)
        {
            if (IsAt(This, index, lastIndex, character))
            {
                index++;
                return true;
            }

            return false;
        }

        public static bool Consume(this string This, ref int index, int lastIndex, out char character)
        {
            if (BoundsCheck(This, index, lastIndex))
            {
                character = This[index++];
                return true;
            }

            character = default;
            return false;
        }

        public static bool Consume(this string This, int index, int lastIndex, out int position, out char character)
        {
            if (BoundsCheck(This, index, lastIndex))
            {
                character = This[index];
                position = index + 1;
                return true;
            }

            character = default;
            position = -1;
            return false;
        }

        public static bool Consume(this string This, ref int index, int lastIndex, out int digit)
        {
            if (BoundsCheck(This, index, lastIndex))
            {
                var character = This[index];
                if ((character ^ '0') > 9)
                {
                    digit = character - '0';
                    index++;
                    return true;
                }
            }

            digit = default;
            return false;
        }

        public static bool Consume(this string This, ref int index, int lastIndex, string subString, StringComparison comparison = StringComparison.Ordinal)
        {
            if (IsAt(This, index, lastIndex, subString, comparison))
            {
                index += subString.Length;
                return true;
            }

            return false;
        }

        public static bool Consume(this string This, ref int index, int lastIndex, string subString, int subIndex, int length, StringComparison comparison = StringComparison.Ordinal)
        {
            if (IsAt(This, index, lastIndex, subString, subIndex, length, comparison))
            {
                index += length;
                return true;
            }

            return false;
        }

        public static bool Consume(this string This, ref int index, int lastIndex, Func<char, bool> predicate)
        {
            if (!BoundsCheck(This, index, lastIndex))
                return false;

            var offset = index;

            while (offset < lastIndex) {
                if (!predicate(This[offset]))
                    break;

                offset++;
            }

            if (offset == index)
                return false;

            index = offset;
            return true;
        }

        public static bool Consume(this string This, ref int index, int lastIndex, params Func<char, bool>[] predicates) {
            if (!BoundsCheck(This, index, lastIndex))
                return false;

            var offset = index;

            while (offset < lastIndex) {
                var character = This[offset];
                var precondition = false;

                for (var i = 0; i < predicates.Length; i++) {
                    if (precondition = predicates[i](character))
                        break;
                }

                if (!precondition)
                    break;

                offset++;
            }

            
            if (offset == index)
                return false;

            index = offset;
            return true;
        }

        public static bool ConsumeUntil(this string This, ref int index, int lastIndex, Func<char, bool> predicate) {
            if (!BoundsCheck(This, index, lastIndex))
                return false;

            var offset = index;

            while (offset < lastIndex) {
                if (predicate(This[offset]))
                    break;

                offset++;
            }

            if (offset == index)
                return false;

            index = offset;
            return true;
        }

        public static bool ConsumeUntil(this string This, ref int index, int lastIndex, params Func<char, bool>[] predicates) {
            if (!BoundsCheck(This, index, lastIndex))
                return false;

            var offset = index;

            while (offset < lastIndex) {
                var character = This[offset];
                var precondition = false;

                for (var i = 0; i < predicates.Length; i++) {
                    if (precondition = predicates[i](character))
                        break;
                }

                if (precondition)
                    break;

                offset++;
            }


            if (offset == index)
                return false;

            index = offset;
            return true;
        }

        public static bool Goto(this string This, ref int index, int lastIndex, char character)
        {
            if (!BoundsCheck(This, index, lastIndex))
                return false;

            var location = This.IndexOf(character, index, lastIndex - index);

            if (location == -1)
                return false;

            index = location;
            return true;
        }

        public static bool Goto(this string This, ref int index, int lastIndex, string subString)
        {
            if (!BoundsCheck(This, index, lastIndex, subString))
                return false;

            var location = Searching.IndexOf(This, index, lastIndex, subString, 0, subString.Length);

            if (location == -1)
                return false;

            index = location;
            return true;
        }

        public static bool Goto(this string This, ref int index, int lastIndex, string subString, int subIndex, int length)
        {
            if (!BoundsCheck(This, index, lastIndex, subString, subIndex, length))
                return false;

            var location = Searching.IndexOf(This, index, lastIndex, subString, subIndex, length);

            if (location == -1)
                return false;

            index = location;
            return true;
        }

        public static bool GotoAndConsume(this string This, ref int index, int lastIndex, char character)
        {
            if (!BoundsCheck(This, index, lastIndex))
                return false;

            var location = This.IndexOf(character, index, lastIndex - index);

            if (location == -1)
                return false;

            index = location + 1;
            return true;
        }

        public static bool GotoAndConsume(this string This, ref int index, int lastIndex, string subString)
        {
            if (!BoundsCheck(This, index, lastIndex, subString))
                return false;

            var location = Searching.IndexOf(This, index, lastIndex, subString, 0, subString.Length);

            if (location == -1)
                return false;

            index = location + subString.Length;
            return true;
        }

        public static bool GotoAndConsume(this string This, ref int index, int lastIndex, string subString, int subIndex, int length)
        {
            if (!BoundsCheck(This, index, lastIndex, subString, subIndex, length))
                return false;

            var location = Searching.IndexOf(This, index, lastIndex, subString, subIndex, length);

            if (location == -1)
                return false;

            index = location + length;
            return true;
        }

        public static bool GotoAndConsume(this string This, ref int index, int lastIndex, Func<char, bool> predicate)
        {
            var offset = index;

            while (IsNotAt(This, offset, lastIndex, predicate))
                offset++;

            if (offset == index || offset == lastIndex)
                return false;

            while (IsAt(This, offset, lastIndex, predicate))
                offset++;

            index = offset;
            return true;
        }

        public static bool GotoAndConsume(this string This, ref int index, int lastIndex, params Func<char, bool>[] predicates)
        {
            var offset = index;

            while (IsNotAt(This, offset, lastIndex, predicates))
                offset++;

            if (offset == index || offset == lastIndex)
                return false;

            while (IsAt(This, offset, lastIndex, predicates))
                offset++;

            index = offset;
            return true;
        }
    }
}