using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly {
    public partial class App {
        public static jsObject<Func<string, string>> MatchModifiers = new jsObject<Func<string, string>>() {
            { "escape", (V) => { return V.Escape(); } },
            { "descape", (V) => { return V.Descape(); } },
            { "uriescape", (V) => { return Uri.EscapeDataString(V); } },
            { "uridescape", (V) => { return Uri.UnescapeDataString(V); } },
        };
    }

    public static class StringMatching {
        private static readonly string[] WildChars = new string[] {
            "{", "*", "?", "^", "|", "\\"
        };

        public static bool Compare(this String DataString, String WildString, bool IgnoreCase = false, int DataIndex = 0, int WildIndex = 0) {
            if (string.IsNullOrEmpty(DataString) || string.IsNullOrEmpty(WildString) || (DataString.Length == 1 && WildString.Length > 1)) {
                return false;
            }

            String Data, Wild;

            if (WildString == "*")
                return true;

            if (IgnoreCase) {
                Data = DataString.ToLower();
                Wild = WildString.ToLower();
            }
            else {
                Data = DataString;
                Wild = WildString;
            }
            
            bool Optional = false;
            while (DataIndex < Data.Length && WildIndex < Wild.Length) {
                if (Wild[WildIndex] == '\\') {
                    WildIndex++;
                }

                if (Wild[WildIndex] == '{' && (WildIndex > 0 ? Wild[WildIndex - 1] != '\\' : true)) {
                    string Name, Value;
                    int NameStart = WildIndex + 1, NameEnd = WildIndex;

                    if (!WildString.FindMatchingBrackets("{", "}", WildIndex, ref NameEnd))
                        return false;

                    WildIndex = NameEnd + 1;

                    if (Wild.Compare("\\", WildIndex))
                        WildIndex++;

                    int SubIndex = Wild.FirstPossibleIndex(WildIndex, WildChars);

                    if (SubIndex == -1) {
                        SubIndex = Wild.Length;
                    }

                    if (SubIndex == WildIndex || WildIndex == Wild.Length) {
                        SubIndex = Data.Length;
                    }
                    else {
                        SubIndex = Data.FindSubString(Wild, DataIndex, WildIndex, SubIndex - WildIndex);
                    }

                    if (SubIndex == -1) {
                        if (Optional)
                            break;

                        return false;
                    }

                    Name = WildString.SubString(NameStart, NameEnd - NameStart);
                    Value = Data.SubString(DataIndex, SubIndex - DataIndex);

                    DataIndex = SubIndex;
                }
                else if (Wild[WildIndex] == '(' && Data[DataIndex] == '(') {
                    var SubData = Data.FindMatchingBrackets("(", ")", DataIndex);
                    var SubWild = Wild.FindMatchingBrackets("(", ")", WildIndex);

                    if (SubData.Match(SubWild, IgnoreCase) == null) {
                        if (Optional) {
                            WildIndex += SubWild.Length + 2;
                            DataIndex += SubData.Length + 2;
                        }
                        else {
                            return false;
                        }
                    }

                    DataIndex += SubData.Length + 2;
                    WildIndex += SubWild.Length + 2;
                }
                else if (Wild[WildIndex] == Data[DataIndex] || Wild[WildIndex] == '?') {
                    DataIndex++;
                    WildIndex++;
                }
                else if (Wild[WildIndex] == '|') {
                    Optional = !Optional;
                    WildIndex++;
                }
                else if (Wild[WildIndex] == '*') {
                    WildIndex++;

                    if (WildIndex == Wild.Length) {
                        break;
                    }
                    else if (WildIndex + 1 == Wild.Length) {
                        if (Data[Data.Length - 1] == Wild[WildIndex] || Optional) {
                            break;
                        }
                        return false;
                    }
                    else if (Wild[WildIndex] == '\\') {
                        WildIndex++;
                    }

                    var SubIndex = Wild.FirstPossibleIndex(WildIndex, WildChars);

                    if (SubIndex == -1)
                        SubIndex = Wild.Length;

                    var Value = Wild.SubString(WildIndex, SubIndex - WildIndex);

                    if (SubIndex == WildIndex || WildIndex == Wild.Length) {
                        SubIndex = Data.Length;
                    }
                    else {
                        SubIndex = Data.IndexOf(Value, DataIndex);
                    }

                    if (SubIndex == -1) {
                        if (Optional)
                            break;

                        return false;
                    }

                    DataIndex = SubIndex;
                }
                else if (Wild[WildIndex] == '^') {
                    while (DataIndex < Data.Length && char.IsWhiteSpace(Data[DataIndex])) {
                        DataIndex++;
                    }
                    if (Wild[WildIndex + 1] == '\\') {
                        WildIndex++;
                    }
                    if (Data[DataIndex] != Wild[WildIndex + 1]) {
                        if (Optional)
                            break;

                        return false;
                    }
                    WildIndex++;
                }
                else {
                    return false;
                }
            }

            if (DataIndex == Data.Length && (Wild.Length - WildIndex) < 3) {
                if (Wild[Wild.Length - 1] == '*')
                    return true;
            }

            return DataIndex == Data.Length && WildIndex == Wild.Length;
        }

        private static bool FindNextSection(String This, String Wild, ref int Index, ref int WildIndex) {
            int NextToken = Wild.FirstPossibleIndex(WildIndex, WildChars); 

            if (NextToken == -1) {
                NextToken = Wild.Length;
            }

            var Debug = Wild.SubString(WildIndex, NextToken - WildIndex);

            if ((NextToken - WildIndex) == 0) {
                WildIndex = Wild.Length;
                Index = This.Length;
                return true;
            }

            int NextBlock = This.FindSubString(Wild, Index, WildIndex, NextToken - WildIndex);

            if (NextBlock == -1)
                return false;

            Index = NextBlock;
            return true;
        }

        public static jsObject Match(this String DataString, String WildString, bool IgnoreCase = false, jsObject Storage = null, int Index = 0, int WildIndex = 0, bool Store = true) {
            if (string.IsNullOrEmpty(DataString) || string.IsNullOrEmpty(WildString)) {
                return null;
            }

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

            bool Optional = false;

            for (; Index < This.Length && WildIndex < Wild.Length;) {
                if (Wild[WildIndex] == '\\')
                    WildIndex ++;

                if (Wild[WildIndex] == '|') {
                    Optional = true;
                    WildIndex++;
                    continue;
                }

                if (This[Index] == Wild[WildIndex] || Wild[WildIndex] == '?') {
                    Index++;
                    WildIndex++;
                    continue;
                }
                else
                if (Wild[WildIndex] == '*') {
                    WildIndex++;

                    if (WildIndex == Wild.Length)
                        break;

                    if (Wild[WildIndex] == '\\')
                        WildIndex++;

                    int Sub = Index, SubW = WildIndex;

                    if (FindNextSection(This, Wild, ref Sub, ref SubW)) {
                        Index = Sub;
                        WildIndex = SubW;
                        continue;
                    }
                }
                else
                if (Wild[WildIndex] == '{') {
                    int NameEnd = WildIndex;

                    if (Wild.FindMatchingBrackets("{", "}", WildIndex, ref NameEnd)) {

                        WildIndex++;

                        int Sub = Index, SubW = NameEnd + 1;

                        if (FindNextSection(This, Wild, ref Sub, ref SubW)) {
                            int Mod = Wild.Find(':', WildIndex, NameEnd);

                            if (Mod == -1)
                                Mod = NameEnd;

                            if (Store) {
                                var Name = WildString.SubString(WildIndex, Mod - WildIndex);
                                var Value = DataString.SubString(Index, Sub - Index);

                                if (Mod != NameEnd) {
                                    Value = ModifyMatch(Wild, Mod + 1, Value);
                                }

                                Storage.Set<string>(Name, Value);
                            }
                            WildIndex = SubW;
                            Index = Sub;
                            continue;
                        }
                    }
                }
                else
                if (Wild[WildIndex] == '^') {
                    while (Index < This.Length && char.IsWhiteSpace(This[Index]))
                        Index++;

                    continue;
                }

                if (Optional)
                    break;

                return null;
            }

            return Storage;
        }
        
        private static string ModifyMatch(string Key, int Index, string Value) {
            App.MatchModifiers.ForEach((K, V) =>{
                if (Key.Compare(K, Index)) {
                    Value = V(Value);
                }
            });

            return Value;
        }
    }
}
