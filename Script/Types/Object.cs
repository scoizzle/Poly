using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly.Script.Node {
    public class Object : DataType<jsObject> {
        public new static object Add(jsObject Left, object Right) {
            Variable.Set(Left.Count.ToString(), Left, Right);
            return Left;
        }

        public new static object Subtract(jsObject Left, object Right) {
            if (Right is string) {
                Left[Right.ToString()] = null;
            }
            return null;
        }

        public static new object Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            var Delta = Index;

            if (Text[Delta] == '{') {
                var String = Text.FindMatchingBrackets("{", "}", Delta, false);
                Delta += String.Length + 2;

                var Obj = new jsObject();

                if (Obj.Parse(String)) {
                    Index = Delta;
                    return Obj;
                }
            }
            else if (Text[Delta] == '[') {
                var String = Text.FindMatchingBrackets("[", "]", Delta, true);

                var Obj = new jsObject() {
                    IsArray = true
                };

                if (Obj.Parse(String)) {
                    Index = Delta + String.Length;
                    return Obj;
                }
            }

            return null;
        }
    }
}
