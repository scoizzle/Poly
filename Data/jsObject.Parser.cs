using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Dynamic;

namespace Poly.Data {
    public partial class jsObject : Dictionary<string, object> {
        private object getObjectValue(string val) {
            int ntVal;
            if (int.TryParse(val, out ntVal)) {
                return ntVal;
            }

            bool blVal;
            if (bool.TryParse(val, out blVal)) {
                return blVal;
            }

            float fVal;
            if (float.TryParse(val, out fVal)) {
                return fVal;
            }

            double dbVal;
            if (double.TryParse(val, out dbVal)) {
                return dbVal;
            }

            long lnVal;
            if (long.TryParse(val, out lnVal)) {
                return lnVal;
            }

            return val;
        }

        private bool _Raw(string Text, ref int Index, params string[] Key) {
            var Token = Text.FirstPossible(Index, ",", "}", "]");
            var SubIndex = Text.IndexOf(Token, Index);

            if (SubIndex == -1 || SubIndex == Index)
                SubIndex = Text.Length;

            var Sub = Text.Substring(Index, SubIndex - Index);

            Set(Key, getObjectValue(Sub.Trim()));

            Index += Sub.Length;

            return true;
        }

        private bool _String(string Text, ref int Index, params string[] Key) {
            if (Text[Index] == '"') {
                var Sub = Text.FindMatchingBrackets("\"", "\"", Index);

                Index += Sub.Length + 2;

                Set(Key, Sub.Descape());
            }
            else if (Text[Index] == '\'') {
                var Sub = Text.FindMatchingBrackets("'", "'", Index);

                Index += Sub.Length + 2;

                Set(Key, Sub.Descape());
            }
            else {
                return false;
            }

            return true;
        }

        private bool _Array(string Text, ref int Index, params string[] Key) {
            var SubIndex = 0;
            var This = default(jsObject);

            if (Key.Length == 0) {
                This = this;
            }
            else {
                This = new jsObject() {
                    IsArray = true
                };

                Set(Key, This);
            }

            Text = Text.FindMatchingBrackets("[", "]", Index, true);

            Index += Text.Length;

            Text = Text.Substring(1, Text.Length - 2);

            do {
                if (Text[SubIndex] == ',') {
                    SubIndex++;
                }

                while (SubIndex < Text.Length && char.IsWhiteSpace(Text[SubIndex]))
                    SubIndex++;

                if (SubIndex == Text.Length)
                    break;


                var X = Text[SubIndex];
                var Name = This.Count.ToString();

                if (X == '"' || X == '\'') {
                    if (!This._String(Text, ref SubIndex, Name))
                        return false;
                }
                else if (X == '{') {
                    if (!This._Object(Text, ref SubIndex, Name))
                        return false;
                }
                else if (X == '[') {
                    if (!This._Array(Text, ref SubIndex, Name))
                        return false;
                }
                else {
                    if (!This._Raw(Text, ref SubIndex, Name))
                        return false;
                }
            }
            while (SubIndex < Text.Length);

            return true;
        }

        private bool _Object(string Text, ref int Index, params string[] Key) {
            var SubIndex = 0;
            var This = default(jsObject);

            if (Key.Length == 0) {
                This = this;
            }
            else {
                This = new jsObject();
                Set(Key, This);
            }

            Text = Text.FindMatchingBrackets("{", "}", Index, true);

            Index += Text.Length;

            Text = Text.Substring(1, Text.Length - 2);

            if (Text.Length != 0) {
                do {
                    if (Text[SubIndex] == ',') {
                        SubIndex++;
                    }

                    while (SubIndex < Text.Length && char.IsWhiteSpace(Text[SubIndex]))
                        SubIndex++;

                    if (SubIndex == Text.Length)
                        break;

                    if (!This._NamedValue(Text, ref SubIndex)) {
                        return false;
                    }
                }
                while (SubIndex < Text.Length);
            }
            return true;
        }

        private bool _NamedValue(string Text, ref int Index) {
            var Name = "";
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

            if (Index == -1)
                return false;
            else {
                Index++;

                while (char.IsWhiteSpace(Text[Index]))
                    Index++;
            }


            X = Text[Index];

            if (X == '"' || X == '\'') {
                return _String(Text, ref Index, Name);
            }
            else if (X == '{') {
                return _Object(Text, ref Index, Name);
            }
            else if (X == '[') {
                return _Array(Text, ref Index, Name);
            }
            else {
                return _Raw(Text, ref Index, Name);
            }
        }

        public bool Parse(string Text) {
            int Index = 0;

            if (Text == null) {
                return false;
            }
            else {
                Text = Text.Trim();
            }
            if (Text == string.Empty) {
                return true;
            }

            if (Text.StartsWith("{") && Text.EndsWith("}")) {
                return _Object(Text, ref Index);
            }
            else if (Text.StartsWith("[") && Text.EndsWith("]")) {
                return _Array(Text, ref Index);
            }
            else {
                return _Object("{ " + Text + " }", ref Index);
            }
        }
    }
}
