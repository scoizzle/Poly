using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Poly.Script.Expressions {
    using Nodes;
    public class Try : Expression {
        public Node Node = null, Catch = null;

        public override object Evaluate(Data.jsObject Context) {
            try {
                if (Node != null)
                    return Node.Evaluate(Context);
            }
            catch (Exception Error) {
                if (Catch != null) {
                    Context.Set("Error", Error);

                    if (Catch != null)
                        return Catch.Evaluate(Context);
                }
            }
            return null;
        }

        public override string ToString() {
            return "try " + base.ToString();
        }

        public static new Try Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            if (Text.Compare("try", Index)) {
                var Delta = Index + 3;
                var Try = new Try();
                ConsumeWhitespace(Text, ref Delta);

                Try.Node = Engine.Parse(Text, ref Delta, LastIndex) as Node;
                ConsumeWhitespace(Text, ref Delta);

                if (Text.Compare("catch", Delta)) {
                    Delta += 5;
                    ConsumeWhitespace(Text, ref Delta);

                    Try.Catch = Engine.Parse(Text, ref Delta, LastIndex) as Node;
                }

                Index = Delta;
                return Try;
            }

            return null;
        }
    }
}
