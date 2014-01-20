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

            if (Text.Compare("js", Index)) {
                var Delta = Index + 2;
                ConsumeWhitespace(Text, ref Delta);

                if (Text[Delta] == '{') {
                    var String = Text.FindMatchingBrackets("{", "}", Delta, false);
                    Index = Delta + String.Length + 2;

                    return new jsObject(
                        String
                    );
                }
                else if (Text[Delta] == '[') {
                    var String = Text.FindMatchingBrackets("[", "]", Delta, true);
                    Index = Delta + String.Length;

                    return new jsObject(
                        String
                    ) { IsArray = true };
                }
            }

            return null;
        }
    }
}
