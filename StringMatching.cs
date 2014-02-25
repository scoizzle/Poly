using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly {
    public static class StringMatching {
        private static readonly string[] WildChars = new string[] {
            "{", "*", "?", "^", "|"
        };

        public static bool Compare(this String This, String Wild, bool IgnoreCase = true, int Index = 0) {
            if (string.IsNullOrEmpty(This) || string.IsNullOrEmpty(Wild)) {
                return false;
            }

            if (IgnoreCase) {
                This = This.ToLower();
                Wild = Wild.ToLower();
            }

            int x = Index, y = 0,
                X = This.Length,
                Y = Wild.Length;

            if (Wild == "*")
                return true;

            while (x < X && y < Y) {
                if (Wild[y] == This[x] || Wild[y] == '?') {
                    x++;
                    y++;
                }
                else if (Wild[y] == '*') {
                    y++;

                    if (y == Y) {
                        break;
                    }
                    else if (y + 1 == Y) {
                        if (This[X - 1] == Wild[y]) {
                            return true;
                        }
                        return false;
                    }

                    var rawValue = string.Empty;
                    int tmp = -1, z = -1;

                    rawValue = Wild.FirstPossible(y, WildChars);

                    if (string.IsNullOrEmpty(rawValue)) {
                        rawValue = Wild.Substring(y, Wild.Length - y);
                        z = This.IndexOf(rawValue, x);
                    }
                    else {
                        tmp = Wild.IndexOf(rawValue, y);
                        if (tmp - y > 0) {
                            rawValue = Wild.Substring(y, tmp - y);
                        }
                        z = This.IndexOf(rawValue, x);
                    }

                    if (z == -1)
                        return false;

                    y = Wild.IndexOf(rawValue, y);
                    x = z;
                }
                else if (Wild[y] == '^') {
                    while (x < X && char.IsWhiteSpace(This[x])) {
                        x++;
                    }
                    if (Wild[y + 1] == '\\') {
                        y++;
                    }
                    if (This[x] != Wild[y + 1]) {
                        return false;
                    }
                    y++;
                }
                else {
                    return false;
                }
            }

            if (x == X && (Y - y) < 3) {
                if (Wild[Y - 1] == '*')
                    return true;
            }

            return x == X && y == Y;
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

                    Name = WildString.FindMatchingBrackets("{", "}", WildIndex);

                    if (string.IsNullOrEmpty(Name))
                        break;

                    WildIndex += Name.Length + 2;

                    if (Wild.Compare("\\", WildIndex))
                        WildIndex++;

                    int SubIndex = Wild.FirstPossibleIndex(WildIndex, WildChars);

                    if (SubIndex == -1) {
                        SubIndex = Wild.Length;
                    }

                    Value = Wild.Substring(WildIndex, SubIndex - WildIndex);

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

                    Value = Data.Substring(DataIndex, SubIndex - DataIndex);
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

                    var Value = Wild.Substring(WildIndex, SubIndex - WildIndex);

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
