using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Script.Expressions {
    using Nodes;
    using Expressions;
    class Eval : Expression {
        public Node Node = null;

        public override object Evaluate(Data.jsObject Context) {
            if (Node != null) {
                return Node.Evaluate(Context);
            }
            return null;
        }

        public override string ToString() {
            return string.Format("({0})", Node);
        }

        public static new Eval Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            if (Text.Compare("(", Index)) {
                var Delta = Index;
                var Eval = new Eval();
                var Statement = Text.FindMatchingBrackets("(", ")", Index);

                Eval.Node = Engine.Parse(Statement, 0);

                Delta += Statement.Length + 2;
                ConsumeWhitespace(Text, ref Delta);

                Index = Delta;
                return Eval;
            }

            return null;
        }
    }
}
