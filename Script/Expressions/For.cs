using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Poly.Script.Node {
    public class For : Expression {
        public object Init, Boolean, Modifier;

        public override object Evaluate(Data.jsObject Context) {
            GetValue(Init, Context);

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

                GetValue(Modifier, Context);
            }

            return null;
        }

        public override string ToString() {
            return "for (" + Init.ToString() + "; " + Boolean.ToString() + "; " + Modifier.ToString() + ") " +
                base.ToString();
        }

        public static new For Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            if (Text.Compare("for", Index)) {
                var Delta = Index + 3;
                ConsumeWhitespace(Text, ref Delta);

                if (Text.Compare("(", Delta)) {
                    var For = new For();
                    var Open = Delta + 1;
                    var Close = Delta;

                    ConsumeEval(Text, ref Close);

                    if (Delta == Close)
                        return null;

                    For.Init = Engine.Parse(Text, ref Open, Close) as Node;
                    For.Boolean = Engine.Parse(Text, ref Open, Close) as Node;

                    if (For.Boolean is Between) {
                        var Var = For.Init;

                        For.Modifier = new Assign(Var, new Add(Var, 1));
                        For.Init = new Assign(Var, (For.Boolean as Between).Left);
                        (For.Boolean as Between).Left = Var;
                    }
                    else {
                        For.Modifier = Engine.Parse(Text, ref Open, Close) as Node;
                    }

                    Delta = Close;
                    ConsumeWhitespace(Text, ref Delta);

                    var Exp = Engine.Parse(Text, ref Delta, LastIndex);

                    if (Exp != null) {
                        For.Add(Exp);
                        ConsumeWhitespace(Text, ref Delta);

                        Index = Delta;
                        return For;
                    }
                }
            }

            return null;
        }
    }
}
