using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


namespace Poly.Script.Node {
    public class String : DataType<string> {
        public new static object Add(string Left, object Right) {
            return Left + Right.ToString();
        }

        public new static object Subtract(string Left, object Right) {
            return Left.Replace(Right.ToString(), string.Empty);
        }

        public new static object Multiply(string Left, object Right) {
            if (Right is int) {
                return Left.x((int)Right);
            }
            return null;
        }

        public new static object Equal(string Left, object Right) {
            return string.Equals(Left, Right.ToString());
        }

        public static object Parse(string Text, ref int Index) {
            if (Text[Index] == '"') {
                var String = Text.FindMatchingBrackets("\"", "\"", Index, false);

                Index += String.Length + 2;

                return String;
            }
            else if (Text[Index] == '\'') {
                var String = Text.FindMatchingBrackets("'", "'", Index, false);

                Index += String.Length + 2;

                return String;
            }
            return null;
        }

        public static new object Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            if (Text[Index] == '"') {
                var String = Text.FindMatchingBrackets("\"", "\"", Index, false);

                Index += String.Length + 2;

                return String;
            }
            else if (Text[Index] == '\'') {
                var String = Text.FindMatchingBrackets("'", "'", Index, false);

                Index += String.Length + 2;

                return String;
            }

            return null;
        }
    }
}
