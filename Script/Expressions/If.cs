using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly.Script.Node {
    public class If : Expression {
        public object Boolean = null, Else = null;

        public override object Evaluate(jsObject Context) {
            if (Bool.EvaluateNode(Boolean, Context)) {
                return base.Evaluate(Context);
            }
            else if (Else != null) {
                return GetValue(Else, Context);
            }
            return null;
        }

        public override string ToString() {
            return "if (" + Boolean.ToString() + ")" + base.ToString() + 
                ((Else != null) ?  Else.ToString() : "");
        }

        public static new If Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            if (Text.Compare("if", Index)) {
                var Delta = Index += 2;
                ConsumeWhitespace(Text, ref Delta);

                if (Text.Compare("(", Delta)) {
                    var If = new If();
                    var Open = Delta + 1;
                    var Close = Delta;

                    ConsumeEval(Text, ref Close);

                    if (Delta == Close)
                        return null;

                    If.Boolean = Engine.Parse(Text, ref Open, Close - 1);

                    Delta = Close;
                    ConsumeWhitespace(Text, ref Delta);

                    var Exp = Engine.Parse(Text, ref Delta, LastIndex) as Node;

                    if (Exp != null) {
                        If.Add(Exp);
                        ConsumeWhitespace(Text, ref Delta);

                        if (Text.Compare("else", Delta)) {
                            Delta += 4;
                            ConsumeWhitespace(Text, ref Delta);
                            If.Else = Engine.Parse(Text, ref Delta, LastIndex) as Node;
                        }
                        Index = Delta;
                        return If;
                    }
                }
            }

            return null;
        }
    }
}
