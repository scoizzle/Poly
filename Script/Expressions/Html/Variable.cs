using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Script.Expressions.Html {
    using Data;
    using Nodes;

    public class Variable : Element {
        public Node Value;

        public override void Evaluate(StringBuilder Output, jsObject Context) {
            if (Value != null) {
                var Result = Value.Evaluate(Context);

                if (Result != null)
                    Output.Append(Result);
            }
        }

        new public static Node Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            if (Text.Compare('@', Index)) {
                var Delta = ++Index;
                ConsumeValidName(Text, ref Delta);

                var Node = Engine.Parse(Text, ref Index, Delta);
                if (Node == null)
                    return null;

                ConsumeWhitespace(Text, ref Delta);
                if (Text.Compare('{', Delta)) {
                    var Start = Delta;
                    var End = Delta;

                    if (Text.FindMatchingBrackets("{", "}", ref Start, ref End, true)) {
                        Index = End + 1;

                        return new Template() {
                            Value = Node,
                            Format = Document.Parse(Engine, Text, ref Start, End) as Element
                        };
                    }
                }
                else {
                    return new Variable() { 
                        Value = Node 
                    };
                }
            }
            return null;
        }
    }
}
