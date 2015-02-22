using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Dynamic;

namespace Poly.Data {
    public partial class jsObject : Dictionary<string, object> {
        public static string _Raw(string Text, ref int Index, int LastIndex) {
            var C = Text[Index];

            if ((C > '9' || C < '0') && (C != 't' && C != 'T') && (C != 'f' && C != 'F') && C != '@')
                return null;

            var SubIndex = Text.IndexOf(Text.FirstPossible(Index, ',', '}', ']'), Index);

            if (SubIndex == -1 || SubIndex == Index)
                SubIndex = LastIndex;

            var Sub = Text.Substring(Index, SubIndex - Index);

            Index = SubIndex;

            return Sub;
        }

        private static string _String(string Text, ref int Index, int LastIndex) {
            if (Text.Compare("'", Index)) {
                var Value = Text.FindMatchingBrackets("'", "'", Index);

                Index += Value.Length + 2;

                return Value.Descape();
            }

            if (Text.Compare("\"", Index)) {
                var Value = Text.FindMatchingBrackets("\"", "\"", Index);

                Index += Value.Length + 2;

                return Value.Descape();
            }

            return null;
        }

        private static jsObject _Array(string Text, ref int Index, int LastIndex, jsObject Storage) {
            int Open = Index, Close = Index;

            if (Text.FindMatchingBrackets("[", "]", ref Open, ref Close)) {
                do {
                    while (Open < Close && (char.IsWhiteSpace(Text[Open]) || Text[Open] == ','))
                        Open++;

                    if (Open == Close)
                        break;

                    var X = Text[Open];
                    var Name = Storage.Count.ToString();

                    if (X == '"' || X == '\'') {
                        Storage.Set(Name, _String(Text, ref Open, Close));
                    }
                    else if (X == '{') {
                        Storage.Set(Name, _Object(Text, ref Open, Close, jsObject.NewObject()));
                    }
                    else if (X == '[') {
                        Storage.Set(Name, _Array(Text, ref Open, Close, jsObject.NewArray()));
                    }
                    else {
                        Storage.Set(Name, _Raw(Text, ref Open, Close));
                    }
                }
                while (Open < Close);

                Index = Close + 1;
                return Storage;
            }
            return null;
        }

        private static jsObject _Object(string Text, ref int Index, int LastIndex, jsObject Storage) {
            int Open = Index, Close = Index;

            if (Text.FindMatchingBrackets("{", "}", ref Open, ref Close)) {
                do {
                    if (Text[Open] == ',') {
                        Open++;
                    }

                    while (Open < Close && char.IsWhiteSpace(Text[Open]))
                        Open++;

                    if (Open == Close)
                        break;

                    var Obj = _NamedValue(Text, ref Open, Close, Storage);

                    if (Obj == null) {
                        return null;
                    }
                }
                while (Open < Close);

                Index = Close + 1;
                return Storage;
            }
            return null;
        }

        private static jsObject _NamedValue(string Text, ref int Index, int LastIndex, jsObject Storage) {
            var Name = "";

            while ((char.IsWhiteSpace(Text[Index]) || Text[Index] == ',') && Index < LastIndex)
                Index++;

            var X = Text[Index];

            if (X == '"') {
                Name = Text.FindMatchingBrackets("\"", "\"", Index);

                Index += Name.Length + 2;
            }
            else if (X == '\'') {
                Name = Text.FindMatchingBrackets("'", "'", Index);

                Index += Name.Length + 2;
            }
            else {
                Name = Text.Substring("", ":", Index, false).Trim();
            }

            Index = Text.IndexOf(':', Index);

            if (Index != -1) {
                Index++;

                while (char.IsWhiteSpace(Text[Index]))
                    Index++;

                X = Text[Index];

                if (X == '"' || X == '\'') {
                    Storage.Set(Name, _String(Text, ref Index, LastIndex));
                }
                else if (X == '{') {
                    Storage.Set(Name, _Object(Text, ref Index, LastIndex, jsObject.NewObject()));
                }
                else if (X == '[') {
                    Storage.Set(Name, _Array(Text, ref Index, LastIndex, jsObject.NewArray()));
                }
                else {
                    Storage.Set(Name, _Raw(Text, ref Index, LastIndex));
                }

                return Storage;
            }

            return null;
        }

        public static bool Parse(string Text, int Index, jsObject Storage = null) {
            if (string.IsNullOrEmpty(Text))
                return false;
            
            if (Index < 0 || Index >= Text.Length)
                return false;

            while (char.IsWhiteSpace(Text[Index]) && Index < Text.Length - 1)
                Index++;

            var X = Text[Index];
                
            if (X == '{') {
                return _Object(Text, ref Index, Text.Length, Storage) != null;
            }
            else if (X == '[') {
                return _Array(Text, ref Index, Text.Length, Storage) != null;
            }
            else {
                return _Object("{" + Text + "}", ref Index, Text.Length + 2, Storage) != null;
            }
        }

        public bool Parse(string Text) {
            return Parse(Text, 0, this);
        }
    }
}
