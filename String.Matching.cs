using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly {
    public static class StringMatching {
        public static readonly string[] WildChars = new string[] {
            "{", "*", "?", "^", "\\", "]"
        };

        public static jsObject<Func<string, string>> Modifiers = new jsObject<Func<string, string>>() {
            { "escape", (V) => { return V.Escape(); }},
            { "descape", (V) => { return V.Descape(); }},
            { "uriEscape", (V) => { return Uri.EscapeDataString(V); }},
            { "uriDescape", (V) => { return Uri.UnescapeDataString(V); }},
            { "toUpper", (V) => { return V.ToUpper(); }},
            { "toLower", (V) => { return V.ToLower(); }},
        };

        public static Dictionary<char, Func<char, bool>> MatchValidityCheckers = new Dictionary<char, Func<char, bool>>() {
            { 'a', (c) => { return char.IsLetter(c); }},
            { 'n', (c) => { return char.IsNumber(c); }},
            { 'p', (c) => { return char.IsPunctuation(c); }},
            { 'w', (c) => { return char.IsWhiteSpace(c); }}
        };
        
        public static bool Compare(this String This, String Wild, bool IgnoreCase = false, int Index = 0) {
            return Match(This, Wild, IgnoreCase, null, 0, false) != null;
        }

        public static jsObject Match(this String Data, String Wild, bool IgnoreCase = false, jsObject Storage = null, int Index = 0, bool Store = true) {
            int Offset = 0, DataLen = Data.Length, Wildlen = Wild.Length;
            return Match(Data, Wild, ref Index, ref Offset, IgnoreCase, Storage, Store, DataLen, Wildlen);
        }

        private static jsObject Match(this String DataString, String WildString, ref int Index, ref int Offset, bool IgnoreCase = false, jsObject Storage = null, bool Store = true, int DataLen = 0, int WildLen = 0) {
            if (string.IsNullOrEmpty(DataString) || string.IsNullOrEmpty(WildString))
                return null;

            if (WildString.Length == 1 && DataString.Length > 1 && WildString != "*")
                return null;

            if (Storage == null)
                Storage = new jsObject();

            string This = IgnoreCase ?
                DataString.ToLower() :
                DataString;

            string Wild = IgnoreCase ?
                WildString.ToLower() :
                WildString;

            while (Index < DataLen && Offset < WildLen) {
                if (Wild[Offset] == '\\')
                    Offset++;

                if (Wild[Offset] == '*') {
                    Offset++;

                    if (Offset == WildLen)
                        break;

                    if (Wild[Offset] == '\\')
                        Offset++;

                    int Sub = Index, SubW = Offset;

                    if (!FindNextSection(This, Wild, ref Sub, ref SubW))
                        break;

                    Index = Sub;
                    Offset = SubW;
                }
                else if (Wild[Offset] == '{') {
                    int NameEnd = Offset;

                    if (Wild.FindMatchingBrackets("{", "}", ref Offset, ref NameEnd)) {
                        int Sub = Index, SubW = NameEnd + 1;

                        if (FindNextSection(This, Wild, ref Sub, ref SubW)) {
                            int Mod = Wild.Find(':', Offset, NameEnd);
                            int ModLen = -1;

                            if (Mod == -1)
                                Mod = NameEnd;
                            else
                                ModLen = Wild.Find(':', Mod + 1, NameEnd);

                            if (ModLen == -1)
                                ModLen = NameEnd;

                            var Name = WildString.Substring(Offset, Mod - Offset);
                            var Value = DataString.Substring(Index, Sub - Index);

                            if (Mod != NameEnd) {
                                if (ModLen != NameEnd) {
                                    Value = ModifyMatch(WildString, ModLen + 1, Value);
                                }

                                if (!IsValidMatch(Value, Wild.Substring(Mod + 1, ModLen - Mod - 1))) {
                                    break;
                                }
                            }

                            if (Store) {
                                Storage.Set<string>(Name, Value);
                            }
                            Offset = SubW;
                            Index = Sub;
                        }
                    }
                }
                else if (Wild[Offset] == '[') {
                    int NameEnd = Offset;

                    if (Wild.FindMatchingBrackets("[", "]", ref Offset, ref NameEnd)) {
                        Match(This, Wild, ref Index, ref Offset, IgnoreCase, Storage, Store, DataLen - Index, NameEnd - Offset);
                        Offset = NameEnd + 1;
                    }
                }
                else if (Wild[Offset] == '^') {
                    while (Index < This.Length && char.IsWhiteSpace(This[Index]))
                        Index++;
                    Offset++;
                }
                else if (This[Index] == Wild[Offset] || Wild[Offset] == '?') {
                    Index++;
                    Offset++;
                }
                else break;
            }

            if (Index != DataLen)
                return null;
            
            if (Offset != WildLen){ 
                if (!(Wild.Find('[', Offset, WildLen) == Offset && Wild.Find(']', Index, WildLen) == WildLen - 1))
                    return null;
            }

            return Storage;
        }

        private static bool FindNextSection(String This, String Wild, ref int Index, ref int WildIndex) {
            int NextToken = Wild.FirstPossibleIndex(WildIndex, WildChars);

            if (NextToken == -1) {
                NextToken = Wild.Length;
            }

            if ((NextToken - WildIndex) == 0) {
                WildIndex = Wild.Length;
                Index = This.Length;
                return true;
            }

            int NextBlock = This.FindSubstring(Wild, Index, WildIndex, NextToken - WildIndex);

            if (NextBlock == -1) {
                if (NextToken == Wild.Length && Wild[Wild.Length - 1] == ']') {
                    Index = This.Length;
                    return true;
                }
                return false;
            }                

            Index = NextBlock;
            return true;
        }

        private static bool IsValidMatch(String Data, String Limitations) {
            if (Limitations.Length == 0)
                return true;

            var LimitLength = Limitations.Find('{');

            if (LimitLength != -1) {
                var LenStr = Limitations.FindMatchingBrackets("{", "}", LimitLength, false);
                if (LenStr.Contains(',')) {
                    var MinLen = LenStr.Substring("", ",").ToInt();
                    var MaxLen = LenStr.Substring(",").ToInt();

                    if (Data.Length < MinLen || Data.Length > MaxLen)
                        return false;
                }
                else {
                    if (LenStr.ToInt() != Data.Length)
                        return false;
                }
            }
            else {
                LimitLength = Limitations.Length;
            }

            for (int i = 0; i < Data.Length; i++) {
                bool Valid = false;

                for (int y = 0; y < LimitLength; y++) {
                    if (MatchValidityCheckers.ContainsKey(Limitations[y])) {
                        if (MatchValidityCheckers[Limitations[y]](Data[i])) {
                            Valid = true;
                            break;
                        }
                    }
                }

                if (!Valid)
                    return false;
            }

            return true;
        
        }

        private static string ModifyMatch(string Key, int Index, string Value) {
            Modifiers.ForEach((K, V) => {
                if (Key.Compare(K, Index)) {
                    Value = V(Value);
                }
            });

            return Value;
        }
    }
}
