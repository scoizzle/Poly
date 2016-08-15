﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using Poly.Data;

namespace Poly {
    public static class StringMatching {
        public static readonly char[] SpecialChars = new char[] {
            '{', '*', '?', '^', '\\', '[', '('
        };

        public delegate bool? TestDelegate(char C);
        public delegate string ModDelegate(string Str);

        public static KeyValueCollection<TestDelegate> Tests = new KeyValueCollection<TestDelegate>() {
            { "Alpha", c => char.IsLetter(c) },
            { "Numeric", c => char.IsNumber(c)},
            { "AlphaNumeric", c => char.IsLetterOrDigit(c)},
            { "Punctuation", c => char.IsPunctuation(c)},
            { "Whitespace", c => char.IsWhiteSpace(c)},
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
            { "Trim", s => { return s.Trim(); }}
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

        public static jsObject Match(this String Data, String Wild) {
            if (string.IsNullOrEmpty(Data) || string.IsNullOrEmpty(Wild))
                return null;

            return GetMatcher(Wild).Match(Data);
        }

        public static jsObject Match(this String Data, String Wild, int Index) {
            if (string.IsNullOrEmpty(Data) || string.IsNullOrEmpty(Wild))
                return null;
            
            return GetMatcher(Wild).Match(Data, ref Index);
        }

        public static jsObject Match(this String Data, String Wild, jsObject Storage) {
            if (Data == null || Wild == null)
                return null;

            return GetMatcher(Wild).Match(Data, Storage);
        }

        public static jsObject Match(this String Data, String Wild, int Index, jsObject Storage) {
            if (string.IsNullOrEmpty(Data) || string.IsNullOrEmpty(Wild))
                return null;

            return GetMatcher(Wild).Match(Data, ref Index, Storage);
        }

        public static jsObject MatchAll(this String Data, String Wild) {
            if (string.IsNullOrEmpty(Data) || string.IsNullOrEmpty(Wild))
                return null;

            return GetMatcher(Wild).MatchAll(Data);
        }

        public static jsObject MatchAll(this String Data, String Wild, int Index) {
            if (string.IsNullOrEmpty(Data) || string.IsNullOrEmpty(Wild))
                return null;

            return GetMatcher(Wild).MatchAll(Data, ref Index);
        }

        public static jsObject MatchAll(this String Data, String Wild, jsObject Storage) {
            if (Data == null || Wild == null)
                return null;

            return GetMatcher(Wild).MatchAll(Data, Storage);
        }

        public static jsObject MatchAll(this String Data, String Wild, int Index, jsObject Storage) {
            if (string.IsNullOrEmpty(Data) || string.IsNullOrEmpty(Wild))
                return null;

            return GetMatcher(Wild).MatchAll(Data, ref Index, Storage, false);
        }
        public static jsObject MatchAll(this String Data, String Wild, int Index, jsObject Storage, bool SingleObject) {
            if (string.IsNullOrEmpty(Data) || string.IsNullOrEmpty(Wild))
                return null;

            return GetMatcher(Wild).MatchAll(Data, ref Index, Storage, SingleObject);
        }
    }
}