using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly {
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

        public static jsObject Match(this String DataString, String WildString, bool IgnoreCase = false, jsObject Storage = null, int DataIndex = 0, int WildIndex = 0) {
            if (string.IsNullOrEmpty(DataString) || string.IsNullOrEmpty(WildString) || (DataString.Length == 1 && WildString.Length > 1)) {
                return null;
            }

            String Data, Wild;

            if (IgnoreCase) {
                Data = DataString.ToLower();
                Wild = WildString.ToLower();
            }
            else {
                Data = DataString;
                Wild = WildString;
            }

            if (Storage == null)
                Storage = new jsObject();

            bool Optional = false;
            while (DataIndex < Data.Length && WildIndex < Wild.Length) {
                if (Wild[WildIndex] == '\\') {
                    WildIndex++;
                }

                if (Wild[WildIndex] == '{' && (WildIndex > 0 ? Wild[WildIndex - 1] != '\\' : true)) {
                    string Name, Value;
                    int NameStart = WildIndex + 1, NameEnd = WildIndex;

                    if (!WildString.FindMatchingBrackets("{", "}", WildIndex, ref NameEnd))
                        return null;

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

                        return null;
                    }

                    Name = WildString.SubString(NameStart, NameEnd - NameStart);
                    Value = Data.SubString(DataIndex, SubIndex - DataIndex);

                    if ((NameEnd = Name.IndexOf(':')) > -1) {
                        var Mod = Name.Substring(NameEnd + 1);
                        Name = Name.Substring(0, NameEnd);

                        switch (Mod) {
                            case "escape":
                                Value = Value.Escape();
                                break;

                            case "descape":
                                Value = Value.Descape();
                                break;

                            case "uriescape":
                                Value = Uri.EscapeDataString(Value);
                                break;

                            case "uridescape":
                                Value = Uri.UnescapeDataString(Value);
                                break;
                        }
                    }

                    Storage[Name] = Value;

                    DataIndex = SubIndex;
                }
                else if (Wild[WildIndex] == '(' && Data[DataIndex] == '(') {
                    var SubData = Data.FindMatchingBrackets("(", ")", DataIndex);
                    var SubWild = Wild.FindMatchingBrackets("(", ")", WildIndex);

                    if (SubData.Match(SubWild, IgnoreCase, Storage, 0, 0) == null) {
                        if (Optional)
                            return Storage;

                        return null;
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
                        return null;
                    }
                    else if (Wild[WildIndex] == '\\') {
                        WildIndex ++;
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

                        return null;
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

                        return null;
                    }
                    WildIndex++;
                }
                else {
                    return null;
                }
            }

            return Storage;
        }
    }
}
