namespace Poly {
    public static class StringExtensions {
        public static bool BoundsCheck(this string This, int index) =>
            This != null && index >= 0 && index < This.Length;

        public static bool BoundsCheck(this string This, int index, int last_index) =>
            This != null && index >= 0 && index < last_index && last_index <= This.Length;

        public static bool TraverseForward(this string This, ref int index, int last_index, out char character) {
            if (index < last_index) {
                character = This[index++];
                return true;
            }
            
            character = default;
            return false;
        }

        public static bool Compare(this string This, int index, char character) {
            if (index < 0 || index >= This.Length)
                return false;

            return Character.Compare(This[index], character);
        }

        public static bool Compare(this string This, string sub_string) {
            if (This == null || sub_string == null)
                return false;

            var length = sub_string.Length;
            var last_index = length;
            var index = 0;
            var sub_index = 0;

            if (This.Length != last_index)
                return false;

            do {
                if (!Character.Compare(This[index], sub_string[sub_index]))
                    return false;

                index++;
                sub_index++;
            }
            while (index < last_index);

            return true;
        }

        public static bool Compare(this string This, int index, string sub_string) {
            if (This == null || sub_string == null)
                return false;

            if (index < 0)
                return false;

            var length = sub_string.Length;
            var last_index = index + length;
            var sub_index = 0;

            if (This.Length < last_index)
                return false;

            if (sub_string.Length < length)
                return false;

            do {
                if (!Character.Compare(This[index], sub_string[sub_index]))
                    return false;

                index++;
                sub_index++;
            }
            while (index < last_index);

            return true;
        }

        public static bool Compare(this string This, int index, string sub_string, int sub_index) {
            if (This == null || sub_string == null)
                return false;

            if (index < 0 || index >= This.Length)
                return false;

            if (sub_index < 0 || sub_index >= sub_string.Length)
                return false;

            return Character.Compare(This[index], sub_string[sub_index]);
        }

        public static bool Compare(this string This, int index, string sub_string, int sub_index, int length) {
            if (This == null || sub_string == null)
                return false;

            if (index < 0 || sub_index < 0)
                return false;

            var last_index = index + length;

            if (This.Length < last_index)
                return false;

            if (sub_string.Length < sub_index + length)
                return false;

            do {
                if (!Character.Compare(This[index], sub_string[sub_index]))
                    return false;

                index++;
                sub_index++;
            }
            while (index < last_index);

            return true;
        }

        public static bool Compare(this string This, int index, int last_index, string sub_string, int sub_index, int sub_last_index) {
            if (This == null || sub_string == null)
                return false;

            if (index < 0 || sub_index < 0)
                return false;

            if (This.Length < last_index || sub_string.Length < sub_last_index)
                return false;

            if ((last_index - index) != (sub_last_index - sub_index))
                return false;

            do {
                if (!Character.Compare(This[index], sub_string[sub_index]))
                    return false;

                index++;
                sub_index++;
            }
            while (index < last_index);

            return true;
        }

        public static bool CompareIgnoreCase(this string This, int index, char character) {
            if (index < 0 || index >= This.Length)
                return false;

            return Character.CompareIgnoreCase(This[index], character);
        }

        public static bool CompareIgnoreCase(this string This, string sub_string) {
            if (This == null || sub_string == null)
                return false;

            var length = sub_string.Length;
            var last_index = length;
            var index = 0;
            var sub_index = 0;

            if (This.Length != last_index)
                return false;

            do {
                if (!Character.CompareIgnoreCase(This[index], sub_string[sub_index]))
                    return false;

                index++;
                sub_index++;
            }
            while (index < last_index);

            return true;
        }

        public static bool CompareIgnoreCase(this string This, int index, string sub_string, int sub_index) {
            if (This == null || sub_string == null)
                return false;

            if (index < 0 || index >= This.Length)
                return false;

            if (sub_index < 0 || sub_index >= sub_string.Length)
                return false;

            return Character.CompareIgnoreCase(This[index], sub_string[sub_index]);
        }

        public static bool CompareIgnoreCase(this string This, int index, string sub_string, int sub_index, int length) {
            if (This == null || sub_string == null)
                return false;

            if (index < 0 || sub_index < 0)
                return false;

            var last_index = index + length;

            if (This.Length < last_index)
                return false;

            if (sub_string.Length < sub_index + length)
                return false;

            do {
                if (!Character.CompareIgnoreCase(This[index], sub_string[sub_index]))
                    return false;

                index++;
                sub_index++;
            }
            while (index < last_index);

            return true;
        }

        public static bool CompareIgnoreCase(this string This, int index, int last_index, string sub_string, int sub_index, int sub_last_index) {
            if (This == null || sub_string == null)
                return false;

            if (index < 0 || sub_index < 0)
                return false;

            if (This.Length < last_index || sub_string.Length < sub_last_index)
                return false;

            if ((last_index - index) != (sub_last_index - sub_index))
                return false;

            do {
                if (!Character.CompareIgnoreCase(This[index], sub_string[sub_index]))
                    return false;

                index++;
                sub_index++;
            }
            while (index < last_index);

            return true;
        }

        public static int Find(this string This, string sub_string) {
            if (This == null || sub_string == null)
                return -1;

            return Find(This, 0, This.Length, sub_string, 0, sub_string.Length);
        }

        public static int Find(this string This, int index, string sub_string) {
            if (This == null || sub_string == null)
                return -1;

            return Find(This, index, This.Length, sub_string, 0, sub_string.Length);
        }

        public static int Find(this string This, int index, int last_index, string sub_string, int sub_index, int sub_length) {
            if (This == null || sub_string == null)
                return -1;

            if (index >= last_index || last_index > This.Length)
                return -1;

            var last_possible_index = last_index - sub_length;

            while (index <= last_possible_index) {
                var found = true;
                var index_pos = index;
                var sub_pos = sub_index;

                do {
                    if (!Character.Compare(This[index_pos], sub_string[sub_pos])) {
                        found = false;
                        break;
                    }

                    index_pos++;
                    sub_pos++;
                }
                while (sub_pos < sub_length);

                if (found)
                    return index;

                index++;
            }

            return -1;
        }

        public static bool FindMatchingBrackets(this string This, char open, char close, ref int index, ref int last_index, bool include_selectors = false) {
            if (This == null)
                return false;

            var length =
                last_index < This.Length ? last_index
                                         : This.Length;

            if (index < 0 || index >= length)
                return false;

            index = This.IndexOf(open, index, length - index);

            if (index == -1)
                return false;

            var count = 1;

            for (var position = index + 1; position < length; position++) {
                var character = This[position];

                if (Character.Compare(character, close)) {
                    if (--count == 0) {
                        if (include_selectors) {
                            last_index = position + 1;
                        }
                        else {
                            last_index = position;
                            index++;
                        }

                        return true;
                    }
                }
                else
                if (Character.Compare(character, open)) {
                    count++;
                }
            }

            return false;
        }

        public static bool FindMatchingBrackets(this string This, string open, string close, ref int index, ref int last_index, bool include_selectors = false) {
            if (This == null)
                return false;

            var length = This.Length;

            if (index < 0 || index >= length)
                return false;

            if (index < last_index)
                return false;

            index = Find(This, index, last_index, open, 0, open.Length);

            if (index == -1)
                return false;

            if (last_index < length)
                length = last_index;

            var close_length = close.Length;
            var open_length = open.Length;
            var count = 1;

            for (var position = index + 1; position < length; position++) {
                if (Compare(This, position, close, 0, close_length)) {
                    if (--count == 0) {
                        if (include_selectors) {
                            last_index = position + 1;
                        }
                        else {
                            last_index = position;
                            index++;
                        }

                        return true;
                    }
                }
                else
                if (Compare(This, position, open, 0, open_length)) {
                    count++;
                }
            }

            return false;
        }
    }
}