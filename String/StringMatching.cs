using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using Poly.Data;

namespace Poly {
    public static class StringMatching {
        public static readonly char[] Tokens = new char[] {
            '{', '[', '*', '?', '^', '\\', '`'
        };

        public delegate bool TestDelegate(char C);
        public delegate object ModDelegate(string Str);

        public static KeyValueCollection<TestDelegate> Tests = new KeyValueCollection<TestDelegate>() {
            { "AlphaNumeric", c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') },
            { "Alpha", c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') },
            { "a", c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') },
            { "Lower", c => (c >= 'a' && c <= 'z') },
            { "l", c => (c >= 'a' && c <= 'z') },
            { "Upper", c => (c >= 'A' && c <= 'Z') },
            { "u", c => (c >= 'A' && c <= 'Z') },
            { "Numeric", c => (c >= '0' && c <= '9') },
            { "n", c => (c >= '0' && c <= '9') },
            { "Punctuation", char.IsPunctuation },
            { "p", char.IsPunctuation },
            { "Whitespace", char.IsWhiteSpace },
            { "w", char.IsWhiteSpace }
        };

        public static KeyValueCollection<ModDelegate> Modifiers = new KeyValueCollection<ModDelegate>() {
            { "Escape", StringConversions.Escape },
            { "Descape", StringConversions.Descape },
            { "UrlEscape", StringConversions.UriEscape },
            { "UrlDescape", StringConversions.UriDescape },
            { "MD5", StringConversions.MD5 },
            { "SHA1", StringConversions.SHA1 },
            { "SHA256", StringConversions.SHA256 },
            { "SHA512", StringConversions.SHA512 },
            { "Base64Encode", StringConversions.Base64Encode },
            { "Base64Decode", StringConversions.Base64Decode },
            { "ToUpper", s => { return s.ToUpper(); }},
            { "ToLower", s => { return s.ToLower(); }},
            { "Trim", s => { return s.Trim(); }},
            { "Int", s => int.Parse(s) },
            { "UInt", s => uint.Parse(s) },
            { "Long", s => long.Parse(s) },
            { "ULong", s => ulong.Parse(s) },
            { "Double", s => double.Parse(s) },
            { "Float", s => float.Parse(s) },
            { "Decimal", s => decimal.Parse(s) },
            { "IPAddress", s => System.Net.IPAddress.Parse(s) }
        };

        public static KeyValueCollection<Matcher> Cache;

        static StringMatching() {
            Cache = new KeyValueCollection<Matcher>();
        }

        public static Matcher GetMatcher(string Fmt) {
            Matcher Match;

            try {
                if (Cache.TryGetValue(Fmt, out Match))
                    return Match;

                return Cache[Fmt] = new Matcher(Fmt);
            }
            catch {
                return new Matcher(Fmt);
            }
        }

        public static jsObject Match(this String Data, String Wild, jsObject Storage = null, int Index = 0) {
            if (string.IsNullOrEmpty(Data) || string.IsNullOrEmpty(Wild))
                return null;

            return GetMatcher(Wild).Match(Data, Storage, Index);
        }

        public static jsObject MatchAll(this String Data, String Wild, jsObject Storage = null, int Index = 0) {
            if (string.IsNullOrEmpty(Data) || string.IsNullOrEmpty(Wild))
                return null;

            return GetMatcher(Wild).MatchAll(Data, Storage, Index);
        }

        public static jsObject MatchKeyValuePairs(this String Data, String Wild, jsObject Storage = null, int Index = 0) {
            if (string.IsNullOrEmpty(Data) || string.IsNullOrEmpty(Wild))
                return null;

            return GetMatcher(Wild).MatchKeyValuePairs(Data, Storage, Index);
        }
    }
}
