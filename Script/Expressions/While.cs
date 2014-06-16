using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Threading;

namespace Poly.Script.Node {
    public class While : Expression {
        public object Boolean = null;

        public override object Evaluate(Data.jsObject Context) {
            while (Bool.EvaluateNode(Boolean, Context) && Thread.CurrentThread.ThreadState == ThreadState.Running) {
                foreach (var Node in this.Values) {
                    if (Node is Return)
                        return Node;

                    var Result = GetValue(Node, Context);

                    if (Result == Break)
                        return null;

                    if (Result == Continue)
                        break;
                }
            }            

            return null;
        }

        public override string ToString() {
            return "while (" + Boolean.ToString() + ") " + base.ToString();
        }

        public static new While Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            if (Text.Compare("while", Index)) {
                var Delta = Index + 5;
                ConsumeWhitespace(Text, ref Delta);

                if (Text.Compare("(", Delta)) {
                    var While = new While();
                    var Open = Delta + 1;
                    var Close = Delta;

                    ConsumeEval(Text, ref Close);

                    if (Delta == Close)
                        return null;

                    While.Boolean = Engine.Parse(Text, ref Open, Close);

                    Delta = Close + 1;
                    ConsumeWhitespace(Text, ref Delta);
                    var Exp = Engine.Parse(Text, ref Delta, LastIndex);

                    if (Exp != null) {
                        While.Add(Exp);
                        ConsumeWhitespace(Text, ref Delta);

                        Index = Delta;
                        return While;
                    }
                    
                }
            }

            return null;
        }
    }
}
