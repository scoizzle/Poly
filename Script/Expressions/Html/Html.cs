using System;

namespace Poly.Script.Expressions.Html {
    using Nodes;

    public class Html : Node {
        static Helpers.Parser[] Parsers = new Helpers.Parser[] {
            Variable.Parse,
            Template.Parse,
            Document.Parse,
            Call.Parse,
            ComplexElement.Parse,
            Attribute.Parse,
            StaticValue.Parse
        };

        public static Node Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;
            
            for (int i = 0; i < Parsers.Length; i++) {
                var Val = Parsers[i](Engine, Text, ref Index, LastIndex);

                if (Val != null)
                    return Val;
            }

            return null;
        }

        public static Node Parser(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            if (Text.Compare('`', Index)) {
                var Delta = Index;

                if (Text.FindMatchingBrackets('`', '`', ref Index, ref Delta)) {
                    var Value = Parse(Engine, Text, ref Index, Delta);

                    if (Value != null) {
                        Index = Delta + 2;
                        return Value;
                    }                    
                }
            }
            return null;
        }            
    }
}
