using System;

namespace Poly.Script.Types {
    using Nodes;

	public class String : DataType<string> {
        public string Value;

        public String(string Str) { Value = Str; }

        public override object Evaluate(Data.jsObject Context) {
            return Value;
        }

        public override string ToString() {
            return Value;
        }

        public new static object Add(string Left, object Right) {
            return string.Concat(Left, Right);
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

        public new static bool Equal(string Left, object Right) {
            var RightStr = Right == null ? "" : Right.ToString();

            if (Left.Length != RightStr.Length)
                return false;

            return StringExtensions.Compare(Left, Right.ToString(), 0);
        }

        public static Node Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            if (Text[Index] == '"') {
                var String = Text.FindMatchingBrackets("\"", "\"", Index, false);

                Index += String.Length + 2;

                return new String(String.Descape());
            }
            else if (Text[Index] == '\'') {
                var String = Text.FindMatchingBrackets("'", "'", Index, false);

                Index += String.Length + 2;

                return new String(String.Descape());
            }

            return null;
        }
    }
}
