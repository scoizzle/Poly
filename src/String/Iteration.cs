using System;

namespace Poly {

    public static class StringIteration {
        public static bool Consume(this string This, ref int index, char character) {
            if (StringExtensions.Compare(This, index, character)) {
                index++;
                return true;
            }
            return false;
        }

        public static bool Consume(this string This, ref int index, string sub_string, int sub_index, int length) {
            if (StringExtensions.Compare(This, index, sub_string, sub_index, length)) {
                index += length;
                return true;
            }

            return false;
        }

        public static bool ConsumeIgnoreCase(this string This, ref int index, char character) {
            if (StringExtensions.CompareIgnoreCase(This, index, character)) {
                index++;
                return true;
            }
            return false;
        }

        public static bool ConsumeIgnoreCase(this string This, ref int index, string sub_string, int sub_index, int length) {
            if (StringExtensions.CompareIgnoreCase(This, index, sub_string, sub_index, length)) {
                index += length;
                return true;
            }

            return false;
        }

        public static bool Consume(this string This, ref int index, int last_index, Func<char, bool> f) {
            if (This == null)
                return false;

            if (last_index > This.Length)
                last_index = This.Length;

            if (index < 0 || index >= last_index)
                return false;

            var start = index;
            var position = index;
            var valid = false;

            do {
                valid = f(This[position]);
            }
            while (valid && ++position < last_index);

            if (position != start) {
                index = position;
                return true;
            }

            return false;
        }

        public static bool Consume(this string This, ref int index, int last_index, params Func<char, bool>[] fs) {
            if (This == null)
                return false;

            if (last_index > This.Length)
                last_index = This.Length;

            if (index < 0 || index >= last_index)
                return false;

            var start = index;
            var position = index;
            var possible_count = fs.Length;
            var valid = false;

            do {
                var character = This[position];

                for (var i = 0; i < possible_count; i++) {
                    var f = fs[i];

                    if (f(character)) {
                        valid = true;
                        break;
                    }
                }
            }
            while (valid && position++ < last_index);

            if (position != start) {
                index = position;
                return true;
            }

            return false;
        }

        public static bool ConsumeUntil(this string This, ref int index, int last_index, Func<char, bool> f) {
            if (This == null)
                return false;

            if (last_index > This.Length)
                last_index = This.Length;

            if (index < 0 || index >= last_index)
                return false;

            var start = index;
            var position = index;
            var valid = true;

            do {
                valid = !f(This[position]);
            }
            while (valid && ++position < last_index);

            if (position != start) {
                index = position;
                return true;
            }

            return false;
        }

        public static bool ConsumeUntil(this string This, ref int index, int last_index, params Func<char, bool>[] fs) {
            if (This == null)
                return false;

            if (last_index > This.Length)
                last_index = This.Length;

            if (index < 0 || index >= last_index)
                return false;

            var start = index;
            var position = index;
            var possible_count = fs.Length;
            var valid = true;

            do {
                var character = This[position];

                for (var i = 0; i < possible_count; i++) {
                    var f = fs[i];

                    if (f(character)) {
                        valid = false;
                        break;
                    }
                }
            }
            while (valid && ++position < last_index);

            if (position != start) {
                index = position;
                return true;
            }

            return false;
        }
    }
}