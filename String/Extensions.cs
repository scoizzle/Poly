using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

using Poly;
using Poly.Data;

namespace System {
    public static class StringExtensions {
        public static bool Compare(this string This, int index, char character) {
            if (index < 0 || index >= This.Length)
                return false;

            return This[index] == character;
        }

        public static bool Compare(this string This, string sub_string) {
            return Compare(This, 0, sub_string, 0, sub_string.Length);
        }

        public static bool Compare(this string This, int index, string sub_string) {
            return Compare(This, index, sub_string, 0, sub_string.Length);
        }

        public static bool Compare(this string This, int index, string sub_string, int sub_index) {
            if (This == null || sub_string == null)
                return false;

            if (index < 0 || index >= This.Length)
                return false;

            if (sub_index < 0 || sub_index >= sub_string.Length)
                return false;

            return This[index] == sub_string[sub_index];
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
                if (This[index] != sub_string[sub_index])
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

            return CharExtensions.CompareInvariant(This[index], character);
        }

        public static bool CompareIgnoreCase(this string This, string sub_string) {
            return CompareIgnoreCase(This, 0, sub_string, 0, sub_string.Length);
        }

        public static bool CompareIgnoreCase(this string This, int index, string sub_string, int sub_index) {
            if (This == null || sub_string == null)
                return false;

            if (index < 0 || index >= This.Length)
                return false;

            if (sub_index < 0 || sub_index >= sub_string.Length)
                return false;

            return CharExtensions.CompareInvariant(This[index], sub_string[sub_index]);
        }

        public static bool CompareIgnoreCase(this string This, int index, string sub_string, int sub_index, int length) {
            if (This == null || sub_string == null)
                return false;

            if (index < 0 || sub_index < 0)
                return false;

            var last_index = index + length;

            if (This.Length <= last_index)
                return false;

            if (sub_string.Length <= sub_index + length)
                return false;

            do {
                if (CharExtensions.CompareInvariant(This[index], sub_string[sub_index]))
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
            var sub_first_character = sub_string[sub_index];

            while (index <= last_possible_index) {
                var found = true;

                for (var offset = 0; offset < sub_length; offset++) {
                    if (This[index + offset] != sub_string[sub_index + offset]) {
                        found = false;
                        break;
                    }
                }

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

                if (character == close) {
                    if (--count == 0) {
                        if (include_selectors) {
                            last_index = position + 1;
                        }
                        else {
                            last_index = position;
                            index ++;
                        }

                        return true;
                    }
                }
                else 
                if (character == open) {
                    count ++;
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
                            index ++;
                        }

                        return true;
                    }
                }
                else 
                if (Compare(This, position, open, 0, open_length)) {
                    count ++;
                }
            }

            return false;
        }

        public static string GetFileExtension(this string This) {
            var lastPeriod = This.LastIndexOf('.');

            if (lastPeriod == -1)
                return string.Empty;

            return This.Substring(lastPeriod);
        }

        public static string Until(this string This, char character) {
            var idx = This.IndexOf(character);

            if (idx == -1)
                return null;

            return This.Substring(0, character);
        }
    }
}