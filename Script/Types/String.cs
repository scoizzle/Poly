using System;

namespace Poly.Script.Types {
    using Nodes;

	public class String : Value {
        public readonly static String Empty = new String(string.Empty);

        public string Value;

        public String(string Str) { 
            Value = Str; 
        }

        public override object Evaluate(Data.jsObject Context) {
            return Value;
        }

        public override string ToString() {
            return Value;
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
