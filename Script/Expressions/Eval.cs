using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Script.Node {
    class Eval : Expression {
        public object Node = null;

        public override object Evaluate(Data.jsObject Context) {
            return GetValue(Node, Context);
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

                if (Text.Compare("(", Delta)) {
                    if (Eval.Node is Function) {
                        var Call = new Call(Engine, "") {
                            Function = Eval.Node as Function
                        };
                        var Open = Delta;
                        var Close = Delta;

                        if (Text.FindMatchingBrackets("(", ")", ref Open, ref Close)) {
                            Call.ParseArguments(Engine, Text, Open, Close);
                        }

                        Eval.Node = Call;
                        Delta = Close + 1;
                    }
                }
                Index = Delta;
                return Eval;
            }

            return null;
        }
    }
}
