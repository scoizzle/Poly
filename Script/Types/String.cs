using System;

namespace Poly.Script.Types {
    using Nodes;

	public class String : Value {
        public readonly static StaticValue Empty = new StaticValue(string.Empty);
        
        public static Node Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            if (Text[Index] == '"') {
                var String = Text.FindMatchingBrackets("\"", "\"", Index, false);

                Index += String.Length + 2;

                return new StaticValue(String.Descape());
            }
            else if (Text[Index] == '\'') {
                var String = Text.FindMatchingBrackets("'", "'", Index, false);

                Index += String.Length + 2;

                return new StaticValue(String.Descape());
            }

            return null;
        }
    }
}
